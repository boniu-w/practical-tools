using System;
using MathNet.Numerics.RootFinding;

namespace DnvbStrategy
{
    /// <summary>
    /// DNV-B 剩余寿命计算（使用 MathNet.Numerics 求解方程）
    /// </summary>
    [TestClass]
    public class DnvbCalculator
    {
        private double GetFutemp(double Te)
        {
            if (Te > 0 && Te <= 323.15)
                return 0.0;
            if (Te > 323.15 && Te <= 373.15)
                return (Te - 323.15) * 0.6;
            if (Te > 373.15 && Te <= 383.15)
                return 30.0 + (Te - 323.15) * 0.4;

            throw new InvalidOperationException($"此温度 {Te} 无法得出温度导致的拉伸强度折减");
        }

        public double CalculateRemainingLife(
            double D, double t, double d0, double L0,
            double maop, double smts, double CR, double crlength,
            double c0, double Te, string load,
            double Fx, double My, double au, double F1, double F2)
        {
            const double KELVIN = 273.15;
            double TeAbs = Te + KELVIN;

            double Fndv = F1 * F2;
            double futemp = GetFutemp(TeAbs);
            double fu = (smts - futemp) * au;

            // 目标方程: Psw(dt) - maop = 0
            double Func(double dt)
            {
                double dt_d = d0 + CR * dt;
                double lt_d = L0 + crlength * dt;
                double Q_dt = Math.Sqrt(1 + 0.31 * Math.Pow(lt_d / Math.Sqrt(D * t), 2));

                double temp1 = 2 * t * fu * (1 - dt_d / t);
                double temp2 = (D - t) * (1 - dt_d / (Q_dt * t));
                double Psw_dt = Fndv * temp1 / temp2;

                return Psw_dt - maop;
            }

            // ══════════════════════════════════════════════════
            // 方案一：Brent 求解（区间两端必须异号）
            // ══════════════════════════════════════════════════
            double lowerBound = 0.0;
            double upperBound = 200.0;

            // 先验证区间内是否有根
            double fLower = Func(lowerBound);
            double fUpper = Func(upperBound);
            if (fLower * fUpper <= 0.0)
            {
                return Brent.FindRoot(Func, lowerBound, upperBound, 1e-10, 100);
            }

            // ══════════════════════════════════════════════════
            // 方案二：Newton-Raphson（需要导数，这里用差分近似）
            // 当端点同号时（单调函数），从区间内扫描求解
            // ══════════════════════════════════════════════════
            double FuncDeriv(double x)
            {
                const double h = 1e-6;
                return (Func(x + h) - Func(x - h)) / (2 * h);
            }

            try
            {
                // RobustNewtonRaphson.FindRoot(f, df, lowerBound, upperBound, accuracy, maxIterations)
                return RobustNewtonRaphson.FindRoot(Func, FuncDeriv, lowerBound, upperBound, 1e-10, 100);
            }
            catch
            {
                throw new InvalidOperationException(
                    $"无法在区间 [{lowerBound}, {upperBound}] 内找到根。f(lower)={fLower}, f(upper)={fUpper}");
            }
        }

        [TestMethod]
        public void Main0()
        {
            double result = CalculateRemainingLife(
                D: 219.1,
                t: 10.0,
                d0: 1.0,
                L0: 50.0,
                maop: 8.0,
                smts: 415.0,
                CR: 21,
                crlength: 33,
                c0: 0.0,
                Te: 20.0,
                load: "",
                Fx: 0.0,
                My: 0.0,
                au: 0.9,
                F1: 0.72,
                F2: 1.0
            );

            Console.WriteLine($"剩余寿命: {result:F4} 年");
        }
    }
}