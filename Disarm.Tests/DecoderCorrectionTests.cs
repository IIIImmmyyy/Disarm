using Disarm.InternalDisassembly;
using Xunit.Abstractions;

namespace Disarm.Tests;

// 业务快照无法直接观察 decoder 的 opcode、寄存器视图与未分配编码，故在这里固定这些契约。
public class DecoderCorrectionTests : BaseDisarmTest
{
    public DecoderCorrectionTests(ITestOutputHelper outputHelper) : base(outputHelper) { }

    [Fact]
    public void PermuteOpcodeSeparatesInterleaveTransposeAndUnzip()
    {
        var cases = new (uint Word, Arm64Mnemonic Mnemonic)[]
        {
            (0x4E021820, Arm64Mnemonic.UZP1), (0x4E022820, Arm64Mnemonic.TRN1),
            (0x4E023820, Arm64Mnemonic.ZIP1), (0x4E025820, Arm64Mnemonic.UZP2),
            (0x4E026820, Arm64Mnemonic.TRN2), (0x4E027820, Arm64Mnemonic.ZIP2)
        };
        foreach (var (word, mnemonic) in cases)
        {
            var instruction = DisassembleAndCheckMnemonic(word, mnemonic);
            Assert.Equal(Arm64Register.V1, instruction.Op1Reg);
            Assert.Equal(Arm64Register.V2, instruction.Op2Reg);
            Assert.Equal(Arm64ArrangementSpecifier.SixteenB, instruction.Op0Arrangement);
        }
    }

    [Fact]
    public void ExtractPreservesTheFifthSourceRegisterBit()
    {
        var instruction = DisassembleAndCheckMnemonic(0x6E1F0820, Arm64Mnemonic.EXT);
        Assert.Equal(Arm64Register.V31, instruction.Op2Reg);
        Assert.Equal(Arm64ArrangementSpecifier.SixteenB, instruction.Op2Arrangement);
        Assert.Equal(1, instruction.Op3Imm);
    }

    [Fact]
    public void SquareRootAndReciprocalEstimatesAreDistinctOperations()
    {
        DisassembleAndCheckMnemonic(0x6EA1F820, Arm64Mnemonic.FSQRT);
        DisassembleAndCheckMnemonic(0x6EA1D820, Arm64Mnemonic.FRSQRTE);
        DisassembleAndCheckMnemonic(0x4EA1D820, Arm64Mnemonic.FRECPE);
    }

    [Theory]
    [InlineData(0x4E218820U, Arm64Mnemonic.FRINTN, Arm64ArrangementSpecifier.FourS)]
    [InlineData(0x4EA18820U, Arm64Mnemonic.FRINTP, Arm64ArrangementSpecifier.FourS)]
    [InlineData(0x6EE0F820U, Arm64Mnemonic.FNEG, Arm64ArrangementSpecifier.TwoD)]
    [InlineData(0x6EE1F820U, Arm64Mnemonic.FSQRT, Arm64ArrangementSpecifier.TwoD)]
    public void FloatingMiscSeparatesOperationBitFromPrecision(
        uint word, Arm64Mnemonic mnemonic, Arm64ArrangementSpecifier arrangement)
    {
        var instruction = DisassembleAndCheckMnemonic(word, mnemonic);
        Assert.Equal(arrangement, instruction.Op0Arrangement);
        Assert.Equal(arrangement, instruction.Op1Arrangement);
        Assert.Equal(Arm64Register.V0, instruction.Op0Reg);
        Assert.Equal(Arm64Register.V1, instruction.Op1Reg);
    }

    [Theory]
    [InlineData(0x39800020U, Arm64Mnemonic.LDRSB, Arm64Register.X0, 0)]
    [InlineData(0x39C00020U, Arm64Mnemonic.LDRSB, Arm64Register.W0, 0)]
    [InlineData(0x389FF020U, Arm64Mnemonic.LDURSB, Arm64Register.X0, -1)]
    [InlineData(0x38DFF020U, Arm64Mnemonic.LDURSB, Arm64Register.W0, -1)]
    public void SignedByteLoadTargetWidthDoesNotEqualItsAccessWidth(
        uint word, Arm64Mnemonic mnemonic, Arm64Register target, long offset)
    {
        var instruction = DisassembleAndCheckMnemonic(word, mnemonic);
        Assert.Equal(target, instruction.Op0Reg);
        Assert.Equal(Arm64Register.X1, instruction.MemBase);
        Assert.Equal(offset, instruction.MemOffset);
    }

    [Fact]
    public void SignedByteLoadKeepsWritebackAndRegisterAddressForms()
    {
        // 同一目标位宽遗漏也出现在另外两条寻址路径；修复不能改变原来的 writeback 或地址寄存器。
        var pre = DisassembleAndCheckMnemonic(0x38801C20, Arm64Mnemonic.LDRSB);
        Assert.Equal(Arm64Register.X0, pre.Op0Reg);
        Assert.Equal(Arm64MemoryIndexMode.PreIndex, pre.MemIndexMode);
        Assert.Equal(1, pre.MemOffset);
        var post = DisassembleAndCheckMnemonic(0x38C01420, Arm64Mnemonic.LDRSB);
        Assert.Equal(Arm64Register.W0, post.Op0Reg);
        Assert.Equal(Arm64MemoryIndexMode.PostIndex, post.MemIndexMode);
        Assert.Equal(1, post.MemOffset);
        var register = DisassembleAndCheckMnemonic(0x38A26820, Arm64Mnemonic.LDRSB);
        Assert.Equal(Arm64Register.X0, register.Op0Reg);
        Assert.Equal(Arm64Register.X1, register.MemBase);
        Assert.Equal(Arm64Register.X2, register.MemAddendReg);
    }

    [Fact]
    public void WordAddRejectsShiftBitFiveButPreservesValidBoundaryWidths()
    {
        Assert.Throws<Arm64UndefinedInstructionException>(() => Disassembler.DisassembleSingleInstruction(0x0B0D80FD));
        var word = DisassembleAndCheckMnemonic(0x0B0D7CFD, Arm64Mnemonic.ADD);
        Assert.Equal(31, word.Op3Imm);
        var wide = DisassembleAndCheckMnemonic(0x8B0D80FD, Arm64Mnemonic.ADD);
        Assert.Equal(Arm64Register.X29, wide.Op0Reg);
        Assert.Equal(32, wide.Op3Imm);
    }
}
