using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

class Program
{
    // БД1 (10x10), симетрична, діагональ від'ємна
    static readonly double[,] A =
    {
        { -600, 500,  50, 250, 100, 200,  50,  30,  50, 200 },
        {  500,-600,  50,  50, 250, 100,  80,  60, 150,  40 },
        {   50,  50,-525, 200,  50,  40, 100,  70, 200,  25 },
        {  250,  50, 200,-475,  10,  70,  40,  25,  30,  50 },
        {  100, 250,  50,  10,-425, 100, 150,  40, 100, 250 },
        {  200, 100,  40,  70, 100,-380, 100,  60,  30,  45 },
        {   50,  80, 100,  40, 150, 100,-300,  80,  65,  30 },
        {   30,  60,  70,  25,  40,  60,  80,-250, 100,  90 },
        {   50, 150, 200,  30, 100,  30,  65, 100, -70, 200 },
        {  200,  40,  25,  50, 250,  45,  30,  90, 200, -20 },
    };

    static double[] MatVec(double[,] m, double[] x)
    {
        int n = x.Length;
        var y = new double[n];
        for (int i = 0; i < n; i++)
        {
            double sum = 0;
            for (int j = 0; j < n; j++) sum += m[i, j] * x[j];
            y[i] = sum;
        }
        return y;
    }

    static double Dot(double[] a, double[] b)
    {
        double s = 0;
        for (int i = 0; i < a.Length; i++) s += a[i] * b[i];
        return s;
    }

    // Степеневий метод з відношенням Релея
    static double PowerMethod(double[,] m, out int iterations, double eps = 1e-12, int maxIter = 100000)
    {
        int n = m.GetLength(0);
        var x = new double[n];
        for (int i = 0; i < n; i++) x[i] = 1.0 / Math.Sqrt(n);
        double lamOld = 0, lam = 0;
        iterations = 0;
        for (int k = 1; k <= maxIter; k++)
        {
            iterations = k;
            double[] y = MatVec(m, x);
            lam = Dot(x, y);
            double norm = Math.Sqrt(Dot(y, y));
            for (int i = 0; i < n; i++) x[i] = y[i] / norm;
            if (Math.Abs(lam - lamOld) < eps) break;
            lamOld = lam;
        }
        return lam;
    }

    // Найбільше власне число за значенням: зсув на межу Гершгоріна
    static double LargestEigenvalue(double[,] m, out int iterations)
    {
        int n = m.GetLength(0);
        double s = 0;
        for (int i = 0; i < n; i++)
        {
            double rowSum = 0;
            for (int j = 0; j < n; j++) rowSum += Math.Abs(m[i, j]);
            if (rowSum > s) s = rowSum;
        }
        var b = new double[n, n];
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
                b[i, j] = m[i, j] + (i == j ? s : 0);
        return PowerMethod(b, out iterations) - s;
    }

    // Метод Якобі (перевірка)
    static double[] JacobiEigenvalues(double[,] m)
    {
        int n = m.GetLength(0);
        var a = (double[,])m.Clone();
        for (int rotation = 0; rotation < 10000; rotation++)
        {
            int p = 0, q = 1;
            double max = 0;
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                    if (Math.Abs(a[i, j]) > max) { max = Math.Abs(a[i, j]); p = i; q = j; }
            if (max < 1e-12) break;
            double theta = (a[q, q] - a[p, p]) / (2 * a[p, q]);
            double t = Math.Sign(theta == 0 ? 1 : theta) / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1));
            double c = 1 / Math.Sqrt(t * t + 1);
            double s = t * c;
            double app = a[p, p], aqq = a[q, q], apq = a[p, q];
            a[p, p] = app - t * apq;
            a[q, q] = aqq + t * apq;
            a[p, q] = a[q, p] = 0;
            for (int r = 0; r < n; r++)
            {
                if (r == p || r == q) continue;
                double arp = a[r, p], arq = a[r, q];
                a[r, p] = a[p, r] = c * arp - s * arq;
                a[r, q] = a[q, r] = s * arp + c * arq;
            }
        }
        var ev = new double[n];
        for (int i = 0; i < n; i++) ev[i] = a[i, i];
        Array.Sort(ev);
        return ev;
    }

    // Необов'язковий режим для ЛБ 6: віддати L2 на host по TCP
    static void Serve(int port, double l2)
    {
        var listener = new TcpListener(IPAddress.Any, port);
        listener.Start();
        Console.WriteLine($"\nОчікування підключення host на порту {port}...");
        using var client = listener.AcceptTcpClient();
        using var writer = new StreamWriter(client.GetStream()) { AutoFlush = true };
        writer.WriteLine(l2.ToString("R", CultureInfo.InvariantCulture));
        Console.WriteLine("L2 надіслано на host.");
        listener.Stop();
    }

    static void Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        CultureInfo ci = CultureInfo.InvariantCulture;

        Console.WriteLine("=== slave-ОК ===");
        Console.WriteLine($"Ім'я машини: {Environment.MachineName}");
        Console.WriteLine($"ОС: {RuntimeInformation.OSDescription}");
        Console.WriteLine($"Логічних процесорів: {Environment.ProcessorCount}");
        Console.WriteLine($".NET: {RuntimeInformation.FrameworkDescription}\n");

        var sw = Stopwatch.StartNew();
        double lamAbs = PowerMethod(A, out int it1);
        double L2 = LargestEigenvalue(A, out int it2);
        sw.Stop();

        Console.WriteLine($"Степеневий метод (найбільше за модулем): {lamAbs.ToString("F8", ci)} ({it1} ітерацій)");
        Console.WriteLine($"Степеневий метод зі зсувом (найбільше за значенням): L2={L2.ToString("F8", ci)} ({it2} ітерацій)");

        double[] ev = JacobiEigenvalues(A);
        Console.WriteLine("\nВсі власні числа (метод Якобі):");
        foreach (double e in ev) Console.WriteLine("  " + e.ToString("F8", ci));

        double maxEv = ev[ev.Length - 1];
        Console.WriteLine($"\nПеревірка: max власне число = {maxEv.ToString("F8", ci)}");
        Console.WriteLine($"Похибка |L2-Якобі| = {Math.Abs(L2 - maxEv).ToString("E2", ci)}");
        Console.WriteLine($"Час обчислення (степеневий метод): {sw.Elapsed.TotalMilliseconds.ToString("F3", ci)} мс");
        Console.WriteLine($"\nL2 = {L2.ToString("F6", ci)}");

        // dotnet run -- --serve 5000
        if (args.Length >= 1 && args[0] == "--serve")
        {
            int port = args.Length >= 2 ? int.Parse(args[1]) : 5000;
            Serve(port, L2);
        }
    }
}
