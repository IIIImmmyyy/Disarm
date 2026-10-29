using Disarm.InternalDisassembly;
using Xunit.Abstractions;

namespace Disarm.Tests;

// 访存解码契约必须固定状态寄存器、隐式寄存器对和地址；业务快照无法观察这些信息。
public class MemoryCompletionTests : BaseDisarmTest
{
    public MemoryCompletionTests(ITestOutputHelper outputHelper) : base(outputHelper) { }

    [Fact]
    public void ExclusiveStoreKeepsWordStatusAndByteDataRegisters()
    {
        var instruction = DisassembleAndCheckMnemonic(0x08037FFD, Arm64Mnemonic.STXRB);
        Assert.Equal(Arm64Register.W3, instruction.Op0Reg);
        Assert.Equal(Arm64Register.W29, instruction.Op1Reg);
        Assert.Equal(Arm64OperandKind.Memory, instruction.Op2Kind);
        Assert.Equal(Arm64Register.X31, instruction.MemBase);
    }

    [Fact]
    public void ExclusiveLoadDoesNotTreatUnusedRsAsAStatusOperand()
    {
        // LLVM 将 unused Rs 的非全一值标为 Unpredictable；它不改变实际的 load 操作数。
        var instruction = DisassembleAndCheckMnemonic(0xC8627CFC, Arm64Mnemonic.LDXP);
        Assert.Equal(Arm64Register.X28, instruction.Op0Reg);
        Assert.Equal(Arm64Register.X31, instruction.Op1Reg);
        Assert.Equal(Arm64OperandKind.Memory, instruction.Op2Kind);
        Assert.Equal(Arm64OperandKind.None, instruction.Op3Kind);
    }

    [Fact]
    public void CompareSwapPairExpandsBothConsecutiveRegisterPairs()
    {
        var instruction = DisassembleAndCheckMnemonic(0x4862FCFC, Arm64Mnemonic.CASPAL);
        Assert.Equal(Arm64Register.X2, instruction.Op0Reg);
        Assert.Equal(Arm64Register.X3, instruction.Op1Reg);
        Assert.Equal(Arm64Register.X28, instruction.Op2Reg);
        Assert.Equal(Arm64Register.X29, instruction.Op3Reg);
        Assert.Equal(Arm64OperandKind.Memory, instruction.Op4Kind);
        Assert.Equal(Arm64Register.X7, instruction.MemBase);
        Assert.EndsWith("X2, X3, X28, X29, [X7]", instruction.ToString());
    }

    [Fact]
    public void AtomicMemoryOrderingAndSubwordSuffixArePreserved()
    {
        var instruction = DisassembleAndCheckMnemonic(0x78E303FD, Arm64Mnemonic.LDADDALH);
        Assert.Equal(Arm64Register.W3, instruction.Op0Reg);
        Assert.Equal(Arm64Register.W29, instruction.Op1Reg);
        Assert.Equal(Arm64Register.X31, instruction.MemBase);
    }

    [Theory]
    [InlineData(0x389FFBFD, Arm64Mnemonic.LDTRSB)]
    [InlineData(0x199FF3FD, Arm64Mnemonic.LDAPURSB)]
    public void SignedByteLoadsKeepSignedOffsetAndWideTarget(uint word, Arm64Mnemonic mnemonic)
    {
        var instruction = DisassembleAndCheckMnemonic(word, mnemonic);
        Assert.Equal(Arm64Register.X29, instruction.Op0Reg);
        Assert.Equal(Arm64Register.X31, instruction.MemBase);
        Assert.Equal(-1, instruction.MemOffset);
    }

    [Fact]
    public void AuthenticatedLoadReconstructsSplitSignedImmediateAndWriteback()
    {
        var instruction = DisassembleAndCheckMnemonic(0xF8600FFD, Arm64Mnemonic.LDRAA);
        Assert.Equal(Arm64Register.X29, instruction.Op0Reg);
        Assert.Equal(Arm64Register.X31, instruction.MemBase);
        Assert.Equal(-4096, instruction.MemOffset);
        Assert.Equal(Arm64MemoryIndexMode.PreIndex, instruction.MemIndexMode);
    }
}
