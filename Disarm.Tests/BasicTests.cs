using Xunit.Abstractions;

namespace Disarm.Tests;

public class BasicTests : BaseDisarmTest
{
    public BasicTests(ITestOutputHelper testOutputHelper) : base(testOutputHelper) { }

    [Fact]
    public void TestInvalidInstructionPreservesAddressWhileContinuing()
    {
        // FABS 的 size=11、Q=0 仍应拒绝；IgnoreErrors 只允许继续，不得丢失失败位置。
        byte[] bytes = { 0x1F, 0x20, 0x03, 0xD5, 0x00, 0xF9, 0xE0, 0x0E, 0xC0, 0x03, 0x5F, 0xD6 };
        var result = Disassembler.Disassemble(bytes.AsSpan(), 0x1000, out var end,
            Disassembler.Options.IgnoreErrors);

        Assert.Equal(3, result.Count);
        Assert.Equal(Arm64Mnemonic.NOP, result[0].Mnemonic);
        Assert.Equal(Arm64Mnemonic.INVALID, result[1].Mnemonic);
        Assert.Equal(Arm64Mnemonic.RET, result[2].Mnemonic);
        Assert.Equal(0x1004UL, result[1].Address);
        Assert.Equal(0x1008UL, result[2].Address);
        Assert.Equal(0x100CUL, end);
    }

    [Fact]
    public void TestDisassembleEntireBody()
    {
        var result = Disassembler.Disassemble(TestBodies.IncludesPcRelAddressing, 0);

        foreach (var instruction in result)
        {
            OutputHelper.WriteLine(instruction.ToString());
        }
    }

    [Fact]
    public void TestLongerBody()
    {
        var result = Disassembler.Disassemble(TestBodies.HasABadBitMask, 0);

        foreach (var instruction in result)
        {
            OutputHelper.WriteLine(instruction.ToString());
        }
    }

    [Fact]
    public unsafe void TestOverloads()
    {
        byte[] byteArray = TestBodies.HasABadBitMask;
        ReadOnlySpan<byte> span = byteArray;
        ReadOnlyMemory<byte> memory = byteArray;
        fixed (byte* bytePointer = byteArray)
        {
            using var byteArrayEnumerator = Disassembler.Disassemble(byteArray, 0).GetEnumerator();
            using var spanEnumerator = Disassembler.Disassemble(span, 0).GetEnumerator();
            using var spanListEnumerator = Disassembler.Disassemble(span, 0, out _).GetEnumerator();
            using var memoryEnumerator = Disassembler.Disassemble(memory, 0).GetEnumerator();
            using var bytePointerEnumerator = Disassembler.Disassemble(bytePointer, byteArray.Length, 0).GetEnumerator();

            while (byteArrayEnumerator.MoveNext())
            {
                Assert.True(spanEnumerator.MoveNext());
                Assert.True(spanListEnumerator.MoveNext());
                Assert.True(memoryEnumerator.MoveNext());
                Assert.True(bytePointerEnumerator.MoveNext());
                
                var expected = byteArrayEnumerator.Current;
                Assert.Equal(expected, spanEnumerator.Current);
                Assert.Equal(expected, spanListEnumerator.Current);
                Assert.Equal(expected, memoryEnumerator.Current);
                Assert.Equal(expected, bytePointerEnumerator.Current);
            }
            
            Assert.False(spanEnumerator.MoveNext());
            Assert.False(spanListEnumerator.MoveNext());
            Assert.False(memoryEnumerator.MoveNext());
            Assert.False(bytePointerEnumerator.MoveNext());
        }
    }
}
