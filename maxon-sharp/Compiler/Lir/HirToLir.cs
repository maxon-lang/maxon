using MaxonSharp.Hir;

namespace MaxonSharp.Lir;

public class HirToLir
{
    private readonly Dictionary<int, LirVReg> _valueMap = new();
    private int _nextVRegId;

    public LirModule Lower(HirModule hirModule)
    {
        var functions = new List<LirFunction>();
        foreach (var func in hirModule.Functions)
        {
            functions.Add(LowerFunction(func));
        }
        return new LirModule(functions);
    }

    private LirFunction LowerFunction(HirFunction func)
    {
        _valueMap.Clear();
        _nextVRegId = 0;

        var blocks = new List<LirBlock>();
        foreach (var block in func.Blocks)
        {
            blocks.Add(LowerBlock(block));
        }

        return new LirFunction(func.Name, blocks);
    }

    private LirBlock LowerBlock(HirBlock block)
    {
        var instructions = new List<LirInstr>();

        foreach (var instr in block.Instructions)
        {
            LowerInstruction(instr, instructions);
        }

        return new LirBlock(block.Label, instructions);
    }

    private void LowerInstruction(HirInstr instr, List<LirInstr> instructions)
    {
        switch (instr)
        {
            case HirConstInt constInt:
                var dest = GetOrCreateVReg(constInt.Dest.Id);
                instructions.Add(new LirMov(dest, new LirImmediate(constInt.Value)));
                break;

            case HirRet ret:
                LirValue? value = null;
                if (ret.Value != null)
                {
                    value = GetVReg(ret.Value.Id);
                }
                instructions.Add(new LirRet(value));
                break;
        }
    }

    private LirVReg GetOrCreateVReg(int hirValueId)
    {
        if (!_valueMap.TryGetValue(hirValueId, out var vreg))
        {
            vreg = new LirVReg(_nextVRegId++);
            _valueMap[hirValueId] = vreg;
        }
        return vreg;
    }

    private LirVReg GetVReg(int hirValueId)
    {
        return _valueMap[hirValueId];
    }
}
