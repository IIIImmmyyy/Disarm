using Disarm.InternalDisassembly;
using Xunit.Abstractions;

namespace Disarm.Tests;

public class FloatingPointTests : BaseDisarmTest
{
    public FloatingPointTests(ITestOutputHelper outputHelper) : base(outputHelper)
    {
    }

    [Fact]
    public void ImmediateDoublePreservesHighFractionBits()
    {
        // 直接验证解码器发布的 64 位尾数，避免后续 IR/代码生成掩盖 uint 位移截断。
        var twoAndHalf = DisassembleAndCheckMnemonic(0x1E609001, Arm64Mnemonic.FMOV);
        Assert.Equal(Arm64Register.D1, twoAndHalf.Op0Reg);
        Assert.Equal(Arm64OperandKind.FloatingPointImmediate, twoAndHalf.Op1Kind);
        Assert.Equal(0x4004000000000000L, BitConverter.DoubleToInt64Bits(twoAndHalf.Op1FpImm));

        var three = DisassembleAndCheckMnemonic(0x1E611001, Arm64Mnemonic.FMOV);
        Assert.Equal(0x4008000000000000L, BitConverter.DoubleToInt64Bits(three.Op1FpImm));
    }
    
    [Fact]
    public void DataProcessingThreeSourceTests()
    {
        var insn = DisassembleAndCheckMnemonic(0x1F031041, Arm64Mnemonic.FMADD);
        
        Assert.Equal(Arm64OperandKind.Register, insn.Op0Kind);
        Assert.Equal(Arm64OperandKind.Register, insn.Op1Kind);
        Assert.Equal(Arm64OperandKind.Register, insn.Op2Kind);
        Assert.Equal(Arm64OperandKind.Register, insn.Op3Kind);
        
        Assert.Equal(Arm64Register.S1, insn.Op0Reg);
        Assert.Equal(Arm64Register.S2, insn.Op1Reg);
        Assert.Equal(Arm64Register.S3, insn.Op2Reg);
        Assert.Equal(Arm64Register.S4, insn.Op3Reg);
        
        Assert.Equal("0x00000000 FMADD S1, S2, S3, S4", insn.ToString());
        
        insn = DisassembleAndCheckMnemonic(0x1F439041, Arm64Mnemonic.FMSUB);
        
        Assert.Equal(Arm64Register.D1, insn.Op0Reg);
        Assert.Equal(Arm64Register.D2, insn.Op1Reg);
        Assert.Equal(Arm64Register.D3, insn.Op2Reg);
        Assert.Equal(Arm64Register.D4, insn.Op3Reg);
        
        insn = DisassembleAndCheckMnemonic(0x1FE20C20, Arm64Mnemonic.FNMADD);
        
        Assert.Equal(Arm64Register.H0, insn.Op0Reg);
        Assert.Equal(Arm64Register.H1, insn.Op1Reg);
        Assert.Equal(Arm64Register.H2, insn.Op2Reg);
        Assert.Equal(Arm64Register.H3, insn.Op3Reg);

        DisassembleAndCheckMnemonic(0x1F228C20, Arm64Mnemonic.FNMSUB);
    }

}
