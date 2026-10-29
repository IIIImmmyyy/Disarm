using Disarm.InternalDisassembly;
using Xunit.Abstractions;

namespace Disarm.Tests;

// 直接检查编码到操作数的契约；生成 C# 快照不能观察 lane arrangement 和立即数解码。
public class SimdCompletionTests : BaseDisarmTest
{
    public SimdCompletionTests(ITestOutputHelper outputHelper) : base(outputHelper) { }

    [Fact]
    public void VectorShiftLeftPreservesLaneAndImmediate()
    {
        var instruction = DisassembleAndCheckMnemonic(0x4F2154E2, Arm64Mnemonic.SHL);
        Assert.Equal(Arm64Register.V2, instruction.Op0Reg);
        Assert.Equal(Arm64Register.V7, instruction.Op1Reg);
        Assert.Equal(Arm64ArrangementSpecifier.FourS, instruction.Op0Arrangement);
        Assert.Equal(Arm64ArrangementSpecifier.FourS, instruction.Op1Arrangement);
        Assert.Equal(Arm64OperandKind.Immediate, instruction.Op2Kind);
        Assert.Equal(1, instruction.Op2Imm);
    }

    [Fact]
    public void VectorIntegerNegationIsNotFloatingPointComparison()
    {
        var instruction = DisassembleAndCheckMnemonic(0x6EA0B842, Arm64Mnemonic.NEG);
        Assert.Equal(Arm64ArrangementSpecifier.FourS, instruction.Op0Arrangement);
        Assert.Equal(Arm64ArrangementSpecifier.FourS, instruction.Op1Arrangement);
        Assert.Equal(Arm64OperandKind.None, instruction.Op2Kind);
        Assert.Equal(Arm64MnemonicCategory.SimdVectorMath, instruction.MnemonicCategory);
    }

    [Fact]
    public void VectorReductionWritesScalarRegister()
    {
        var instruction = DisassembleAndCheckMnemonic(0x4EB1B800, Arm64Mnemonic.ADDV);
        Assert.Equal(Arm64Register.S0, instruction.Op0Reg);
        Assert.Equal(Arm64ArrangementSpecifier.None, instruction.Op0Arrangement);
        Assert.Equal(Arm64Register.V0, instruction.Op1Reg);
        Assert.Equal(Arm64ArrangementSpecifier.FourS, instruction.Op1Arrangement);
    }

    [Fact]
    public void HalfPrecisionVectorArithmeticPreservesDistinctSources()
    {
        var instruction = DisassembleAndCheckMnemonic(0x4E421461, Arm64Mnemonic.FADD);
        Assert.Equal(Arm64Register.V1, instruction.Op0Reg);
        Assert.Equal(Arm64Register.V3, instruction.Op1Reg);
        Assert.Equal(Arm64Register.V2, instruction.Op2Reg);
        Assert.Equal(Arm64ArrangementSpecifier.EightH, instruction.Op0Arrangement);
        Assert.Equal(Arm64ArrangementSpecifier.EightH, instruction.Op1Arrangement);
        Assert.Equal(Arm64ArrangementSpecifier.EightH, instruction.Op2Arrangement);
    }

    [Fact]
    public void HalfPrecisionVectorUnaryUsesHalfLanes()
    {
        var instruction = DisassembleAndCheckMnemonic(0x4EF8F841, Arm64Mnemonic.FABS);
        Assert.Equal(Arm64Register.V1, instruction.Op0Reg);
        Assert.Equal(Arm64Register.V2, instruction.Op1Reg);
        Assert.Equal(Arm64ArrangementSpecifier.EightH, instruction.Op0Arrangement);
        Assert.Equal(Arm64OperandKind.None, instruction.Op2Kind);
    }

    [Fact]
    public void DotProductKeepsByteSourcesAndWordAccumulator()
    {
        var instruction = DisassembleAndCheckMnemonic(0x4E829461, Arm64Mnemonic.SDOT);
        Assert.Equal(Arm64Register.V3, instruction.Op1Reg);
        Assert.Equal(Arm64Register.V2, instruction.Op2Reg);
        Assert.Equal(Arm64ArrangementSpecifier.FourS, instruction.Op0Arrangement);
        Assert.Equal(Arm64ArrangementSpecifier.SixteenB, instruction.Op1Arrangement);
        Assert.Equal(Arm64ArrangementSpecifier.SixteenB, instruction.Op2Arrangement);
    }
}
