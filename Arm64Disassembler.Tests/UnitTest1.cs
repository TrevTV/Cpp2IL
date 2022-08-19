namespace Arm64Disassembler.Tests;

public class UnitTest1
{
    //Example Arm64 code for a basic assembly-level custom attribute generator
    /* Should disassemble to
         stp x20, x19, [sp, #-0x20]
         stp x29, x30, [sp, #0x10]
         add x29, sp, #0x10
         mov x19, x0
         ldr x8, [x19, #8]
         mov x2, xzr
         movz w1, #0x102
         ldr x0, [x8]
         bl #0x13fc400
         ldr x8, [x19, #8]
         mov x1, xzr
         ldr x19, [x8, #8]
         mov x0, x19
         bl #0xf3d6cc
         ldp x29, x30, [sp, #0x10]
         mov x2, xzr
         orr w1, wzr, #1
         mov x0, x19
         ldp x20, x19, [sp], #0x20
         b #0xf3d6d4
    */
    private static byte[] caGenBody =
    {
        0xF4, 0x4F, 0xBE, 0xA9, 0xFD, 0x7B, 0x01, 0xA9, 0xFD, 0x43, 0x00, 0x91, 0xF3, 0x03, 0x00, 0xAA,
        0x68, 0x06, 0x40, 0xF9, 0xE2, 0x03, 0x1F, 0xAA, 0x41, 0x20, 0x80, 0x52, 0x00, 0x01, 0x40, 0xF9,
        0xF8, 0xF0, 0x4F, 0x94, 0x68, 0x06, 0x40, 0xF9, 0xE1, 0x03, 0x1F, 0xAA, 0x13, 0x05, 0x40, 0xF9,
        0xE0, 0x03, 0x13, 0xAA, 0xA6, 0xF5, 0x3C, 0x94, 0xFD, 0x7B, 0x41, 0xA9, 0xE2, 0x03, 0x1F, 0xAA,
        0xE1, 0x03, 0x00, 0x32, 0xE0, 0x03, 0x13, 0xAA, 0xF4, 0x4F, 0xC2, 0xA8, 0xA2, 0xF5, 0x3C, 0x14
    };

    [Fact]
    public void TestDisassembleEntireBody()
    {
        var result = Disassembler.Disassemble(caGenBody.AsSpan(), 0);
    }
}