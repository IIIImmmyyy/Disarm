using Disarm.InternalDisassembly;
using Xunit.Abstractions;

namespace Disarm.Tests;

// 别名转换必须保留运算及操作数契约；生成 C# 快照无法观察错误匹配或显示槽位。
public class AliasCorrectionTests : BaseDisarmTest
{
    public AliasCorrectionTests(ITestOutputHelper outputHelper) : base(outputHelper) { }

    [Fact]
    public void ShiftedOrrIsNotAliasedToRegisterMove()
    {
        // 零源和重复源都只有在移位为零时才能证明 ORR 等价于直接拷贝。
        foreach (var encoding in new uint[] { 0xAA0117E0, 0xAA011420 })
        {
            var instruction = DisassembleAndCheckMnemonic(encoding, Arm64Mnemonic.ORR);
            Assert.Equal(Arm64OperandKind.Register, instruction.Op2Kind);
            Assert.Equal(Arm64Register.X1, instruction.Op2Reg);
            Assert.Equal(Arm64OperandKind.Immediate, instruction.Op3Kind);
            Assert.Equal(5, instruction.Op3Imm);
            Assert.Equal(Arm64ShiftType.LSL, instruction.FinalOpShiftType);
        }
    }

    [Fact]
    public void ProvenRegisterAndVectorMovesKeepTheirSourceViews()
    {
        var scalar = DisassembleAndCheckMnemonic(0xAA0103E0, Arm64Mnemonic.MOV);
        Assert.Equal(Arm64Register.X1, scalar.Op1Reg);
        Assert.Equal(Arm64OperandKind.None, scalar.Op2Kind);

        var vector = DisassembleAndCheckMnemonic(0x4EA11C20, Arm64Mnemonic.MOV);
        Assert.Equal(Arm64Register.V1, vector.Op1Reg);
        Assert.Equal(Arm64ArrangementSpecifier.SixteenB, vector.Op1Arrangement);
        Assert.Equal(Arm64OperandKind.None, vector.Op2Kind);
    }

    [Theory]
    [InlineData(0x93402020U, Arm64Mnemonic.SBFX, Arm64Register.X0, Arm64Register.X1, 9)]
    [InlineData(0x13002020U, Arm64Mnemonic.SBFX, Arm64Register.W0, Arm64Register.W1, 9)]
    [InlineData(0x13007C20U, Arm64Mnemonic.ASR, Arm64Register.W0, Arm64Register.W1, 0)]
    public void SignedBitfieldOnlyUsesExactSignExtensionAliases(
        uint encoding, Arm64Mnemonic mnemonic, Arm64Register destination, Arm64Register source, long width)
    {
        var instruction = DisassembleAndCheckMnemonic(encoding, mnemonic);
        Assert.Equal(destination, instruction.Op0Reg);
        Assert.Equal(source, instruction.Op1Reg);
        Assert.Equal(0, instruction.Op2Imm);
        if (mnemonic == Arm64Mnemonic.SBFX)
            Assert.Equal(width, instruction.Op3Imm);
        else
            Assert.Equal(Arm64OperandKind.None, instruction.Op3Kind);
    }

    [Fact]
    public void CompareDisplaysShiftWithoutMovingTheConsumerMetadata()
    {
        var shifted = DisassembleAndCheckMnemonic(0xEB02143F, Arm64Mnemonic.CMP);
        Assert.Equal(Arm64OperandKind.None, shifted.Op2Kind);
        Assert.Equal(Arm64OperandKind.Immediate, shifted.Op3Kind);
        Assert.Equal(5, shifted.Op3Imm);
        Assert.Equal("0x00000000 CMP X1, X2, LSL #0x5", shifted.ToString());

        var extended = DisassembleAndCheckMnemonic(0xEB224C3F, Arm64Mnemonic.CMP);
        Assert.Equal(Arm64ExtendType.UXTW, extended.FinalOpExtendType);
        Assert.Equal(3, extended.Op3Imm);
        Assert.Equal("0x00000000 CMP X1, W2, UXTW #0x3", extended.ToString());
    }
}
