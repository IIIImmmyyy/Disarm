namespace Disarm.InternalDisassembly;

/// <summary>
/// Resolves packed AArch64 system-register encodings for disassembly text without changing
/// the raw immediate operand contract. The mnemonic is part of the lookup because some
/// encodings use different architectural names for reads and writes.
/// </summary>
internal static class Arm64SystemRegisterNameResolver
{
    public static bool TryResolve(long encoding, Arm64Mnemonic mnemonic, out string name)
    {
        name = (encoding, mnemonic) switch
        {
            (0xDE82, Arm64Mnemonic.MRS) => "TPIDR_EL0",
            (0xDE82, Arm64Mnemonic.MSR) => "TPIDR_EL0",
            _ => string.Empty
        };

        return name.Length != 0;
    }
}
