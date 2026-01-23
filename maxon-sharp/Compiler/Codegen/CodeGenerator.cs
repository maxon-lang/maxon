using MaxonSharp.Lir;

namespace MaxonSharp.Codegen;

public class CodeGenerator
{
    public byte[] Generate(LirModule module)
    {
        var encoder = new X86Encoder();
        var regAlloc = new RegisterAllocator();

        // For now, just generate code for main function
        var mainFunc = module.Functions.Find(f => f.Name == "main");
        if (mainFunc == null)
        {
            throw new Exception("No main function found");
        }

        var allocation = regAlloc.Allocate(mainFunc);

        // Function prologue
        encoder.Push(Reg.Rbp);
        encoder.MovRegReg(Reg.Rbp, Reg.Rsp);

        // Generate code for each block
        foreach (var block in mainFunc.Blocks)
        {
            foreach (var instr in block.Instructions)
            {
                GenerateInstruction(encoder, instr, allocation);
            }
        }

        return encoder.GetCode();
    }

    private void GenerateInstruction(X86Encoder encoder, LirInstr instr, Dictionary<int, Reg> allocation)
    {
        switch (instr)
        {
            case LirMov mov:
                var destReg = allocation[mov.Dest.Id];
                switch (mov.Src)
                {
                    case LirImmediate imm:
                        encoder.MovRegImm(destReg, imm.Value);
                        break;
                    case LirVReg vreg:
                        encoder.MovRegReg(destReg, allocation[vreg.Id]);
                        break;
                }
                break;

            case LirRet ret:
                // Move return value to RAX if not already there
                if (ret.Value is LirVReg retVreg)
                {
                    var srcReg = allocation[retVreg.Id];
                    if (srcReg != Reg.Rax)
                    {
                        encoder.MovRegReg(Reg.Rax, srcReg);
                    }
                }
                else if (ret.Value is LirImmediate imm)
                {
                    encoder.MovRegImm(Reg.Rax, imm.Value);
                }

                // Function epilogue
                encoder.MovRegReg(Reg.Rsp, Reg.Rbp);
                encoder.Pop(Reg.Rbp);
                encoder.Ret();
                break;
        }
    }
}
