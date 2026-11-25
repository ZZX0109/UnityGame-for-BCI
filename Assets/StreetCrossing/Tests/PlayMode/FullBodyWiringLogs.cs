using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.TestTools;

namespace StreetCrossing.Tests.PlayMode
{
    /// <summary>
    /// 街道场景每次加载（LoadSceneMode.Single）时，全身接线链
    /// （<c>StreetCrossingFullBodyBinding.EnsureWiring</c> → HandRigDriver / ElenaWristAligner）
    /// 会确定性地输出一组 7 条一次性 info 日志：
    ///   1. [HandRigDriver] HandProportions applied to N bones on ElenaHand_L.
    ///   2. [HandRigDriver] native profile active (N bones, M control bindings).
    ///   3. [HandRigDriver] HandProportions applied to N bones on ElenaHand_R.
    ///   4. [HandRigDriver] native profile active (N bones, M control bindings).
    ///   5. [ElenaWristAligner] L 腕部补偿：… → 0.0000 m（localPosition -= …）。
    ///   6. [ElenaWristAligner] R 腕部补偿：… → 0.0000 m（localPosition -= …）。
    ///   7. [StreetCrossingFullBodyBinding] Elena wrist landmark alignment 2/2 hands (target <2 cm residual).
    /// 这些日志是接线成功的良性诊断输出，顺序固定（V5078c 日志中 7 组完全一致）。
    /// 需要保持 LogAssert.NoUnexpectedReceived 纪律的测试，必须在 LoadSceneAsync 之前
    /// 调用 <see cref="ExpectWiringSequence"/>（LogAssert.Expect 按注册顺序 FIFO 消费，
    /// 每条日志消费一次 Expect，因此每次场景加载都要重新登记一组）。
    /// </summary>
    internal static class FullBodyWiringLogs
    {
        /// <summary>登记一次场景加载会产生的全部 7 条接线日志预期（按发射顺序）。</summary>
        internal static void ExpectWiringSequence()
        {
            LogAssert.Expect(LogType.Log, new Regex(@"\[HandRigDriver\] HandProportions applied to \d+ bones on ElenaHand_[LR]\."));
            LogAssert.Expect(LogType.Log, new Regex(@"\[HandRigDriver\] native profile active \(\d+ bones, \d+ control bindings\)\."));
            LogAssert.Expect(LogType.Log, new Regex(@"\[HandRigDriver\] HandProportions applied to \d+ bones on ElenaHand_[LR]\."));
            LogAssert.Expect(LogType.Log, new Regex(@"\[HandRigDriver\] native profile active \(\d+ bones, \d+ control bindings\)\."));
            LogAssert.Expect(LogType.Log, new Regex(@"\[ElenaWristAligner\] [LR] 腕部补偿：.*"));
            LogAssert.Expect(LogType.Log, new Regex(@"\[ElenaWristAligner\] [LR] 腕部补偿：.*"));
            LogAssert.Expect(LogType.Log, new Regex(@"\[StreetCrossingFullBodyBinding\] Elena wrist landmark alignment \d/2 hands \(target <2 cm residual\)\."));
        }
    }
}
