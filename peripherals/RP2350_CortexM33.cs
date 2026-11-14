//
// Copyright (c) 2026 Muhammad Waleed Badar
// SPDX-License-Identifier: Apache-2.0
//
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.IRQControllers;
using Antmicro.Renode.Peripherals.Miscellaneous;
using Antmicro.Renode.Peripherals.Timers;
using Antmicro.Renode.Time;

namespace Antmicro.Renode.Peripherals.CPU
{
    // One Cortex-M33 core of the RP2350, with the coprocessors Raspberry Pi
    // added. Renode's Cortex-M has no coprocessor port: its translator reports
    // a coprocessor instruction and raises an undefined instruction exception.
    // The report is used to put a hook on the instruction, which from then on
    // carries it out and steps over it.
    //
    // Coprocessor 0 (GPIO) is complete. Of coprocessor 4 (double precision)
    // only the read of the status is modelled, and on coprocessor 7
    // (redundancy) every check passes. CPACR is not consulted, and the "2"
    // encodings (MCR2, MRC2, CDP2, ...) still raise the exception.
    //
    // Renode ends a WFE for SEV and for interrupts. Here a return from an
    // exception sets the event register too. The clearing of the exclusive
    // monitor, which software such as the Pico SDK counts on, cannot be seen,
    // so a core that has waited in WFE for wfeEventPeriod microseconds is
    // given an event. 0 turns that off.
    public class RP2350_CortexM33 : CortexM
    {
        public RP2350_CortexM33(IMachine machine, NVIC nvic, RP2350_SIO sio = null, [NameAlias("id")] uint cpuId = 0, ulong wfeEventPeriod = 1000)
            : base("cortex-m33", machine, nvic, cpuId, enableTrustZone: true, numberOfMPURegions: 8, numberOfSAURegions: 8, numberOfIDAURegions: 0)
        {
            this.sio = sio;
            coprocessorHook = (_, address) => ExecuteCoprocessorInstructions(address);

            AddHookAtInterruptEnd(_ => SetEventFlag(true));

            if(wfeEventPeriod > 0)
            {
                var wfeEvent = new LimitTimer(machine.ClockSource, 1000000, this, "wfeEvent", limit: wfeEventPeriod,
                    direction: Direction.Descending, enabled: false, workMode: WorkMode.OneShot, eventEnabled: true);
                wfeEvent.LimitReached += () => SetEventFlag(true);
                // Told about WFI as well; an event does not end a WFI, it
                // only makes the next WFE return at once.
                AddHookAtWfiStateChange(waiting =>
                {
                    if(waiting)
                    {
                        wfeEvent.ResetValue();
                    }
                    wfeEvent.Enabled = waiting;
                });
            }
        }

        public override void Reset()
        {
            base.Reset();
            saltValid[0] = false;
            saltValid[1] = false;
        }

        // The translator has nothing better than a log line to say that it
        // met an instruction it cannot translate.
        protected override void LogAsCpu(int level, string s)
        {
            if(TryHookReportedInstruction(s))
            {
                return;
            }
            base.LogAsCpu(level, s);
        }

        private bool TryHookReportedInstruction(string report)
        {
            var match = UnsupportedAccess.Match(report);
            if(!match.Success || !uint.TryParse(match.Groups[1].Value, out var coprocessor) || !IsModelled(coprocessor))
            {
                return false;
            }

            // The block being translated ends with the reported instruction,
            // so that is the first of its kind from the PC on.
            var address = PC.RawValue & ~1UL;
            for(var end = address + MaximumBlockBytes; address < end; address += InstructionSize(address))
            {
                if(TryDecode(address, out var instruction) && IsModelled(instruction.Coprocessor))
                {
                    AddHook(address, coprocessorHook);
                    // Leave the execution loop before the block runs; the
                    // hook takes effect on the way back in.
                    RequestReturn();
                    return true;
                }
            }
            return false;
        }

        // Carries out the instruction at the hook and any that follow it
        // directly, so that execution resumes on an ordinary instruction.
        private void ExecuteCoprocessorInstructions(ulong address)
        {
            var next = address;
            while(TryDecode(next, out var instruction) && IsModelled(instruction.Coprocessor))
            {
                if(ConditionPasses())
                {
                    Execute(instruction);
                }
                AdvanceItState();
                next += 4;
            }

            if(next != address)
            {
                PC = next | 1;
            }
        }

        private void Execute(Instruction instruction)
        {
            bool handled;
            switch(instruction.Coprocessor)
            {
            case GpioCoprocessor:
                handled = ExecuteGpio(instruction);
                break;
            case DoubleCoprocessor:
                handled = ExecuteDouble(instruction);
                break;
            default:
                handled = ExecuteRedundancy(instruction);
                break;
            }

            if(!handled && reported.Add(instruction.Encoding))
            {
                this.Log(LogLevel.Error, "Coprocessor {0} instruction 0x{1:X8} at 0x{2:X8} is not modelled; it does nothing and reads as 0",
                    instruction.Coprocessor, instruction.Encoding, instruction.Address);
            }
        }

        // CRm picks the register: 0 and 1 are the two halves of the outputs,
        // 4 and 5 of the output enables, 8 and 9 of the inputs.
        private bool ExecuteGpio(Instruction instruction)
        {
            if(sio == null)
            {
                return false;
            }

            var register = instruction.CRm & ~1u;
            var high = (instruction.CRm & 1) != 0;

            switch(instruction.Kind)
            {
            case InstructionKind.MoveFromCoprocessor:
                if(instruction.Opc1 != 0 || !TryReadGpio(register, out var half))
                {
                    return false;
                }
                WriteResult(instruction.Rt, (uint)(high ? half >> 32 : half));
                return true;

            case InstructionKind.MoveFromCoprocessor64:
                if(instruction.Opc1 != 0 || high || !TryReadGpio(register, out var both))
                {
                    return false;
                }
                SetRegister((int)instruction.Rt, (uint)both);
                SetRegister((int)instruction.Rt2, (uint)(both >> 32));
                return true;

            case InstructionKind.MoveToCoprocessor:
            {
                var value = (ulong)GetRegister((int)instruction.Rt).RawValue;
                if(instruction.Opc1 < 4)
                {
                    // A 32-bit mask for one half of the register.
                    var shift = high ? 32 : 0;
                    return TryWriteGpio(register, (Operation)instruction.Opc1, value << shift, 0xFFFFFFFFUL << shift);
                }
                // A pin number, to toggle, set or clear that pin.
                return instruction.Opc1 != 4 && !high && value < 64
                    && TryWriteGpio(register, (Operation)(instruction.Opc1 - 4), 1UL << (int)value, 1UL << (int)value);
            }

            case InstructionKind.MoveToCoprocessor64:
            {
                var first = (ulong)GetRegister((int)instruction.Rt).RawValue;
                var second = (ulong)GetRegister((int)instruction.Rt2).RawValue;
                if(high)
                {
                    return false;
                }
                if(instruction.Opc1 < 4)
                {
                    // A 64-bit mask.
                    return TryWriteGpio(register, (Operation)instruction.Opc1, first | (second << 32), ulong.MaxValue);
                }
                if(instruction.Opc1 < 8)
                {
                    // A pin number and a bit for it.
                    if(first >= 64)
                    {
                        return false;
                    }
                    var bit = (second & 1) << (int)first;
                    return TryWriteGpio(register, (Operation)(instruction.Opc1 - 4), bit, 1UL << (int)first);
                }
                if(instruction.Opc1 < 12 && second < 2)
                {
                    // A 32-bit mask and the half of the register it is for.
                    var shift = second == 0 ? 0 : 32;
                    return TryWriteGpio(register, (Operation)(instruction.Opc1 - 8), first << shift, 0xFFFFFFFFUL << shift);
                }
                return false;
            }

            default:
                return false;
            }
        }

        private bool TryReadGpio(uint register, out ulong value)
        {
            switch(register)
            {
            case GpioOutputs:
                value = sio.GpioOutputs;
                return true;
            case GpioOutputEnables:
                value = sio.GpioOutputEnables;
                return true;
            case GpioInputs:
                value = sio.GpioInputs;
                return true;
            default:
                value = 0;
                return false;
            }
        }

        // Only the bits under the mask are written by a put.
        private bool TryWriteGpio(uint register, Operation operation, ulong value, ulong mask)
        {
            Func<ulong, ulong> change;
            switch(operation)
            {
            case Operation.Put:
                change = current => (current & ~mask) | (value & mask);
                break;
            case Operation.Xor:
                change = current => current ^ (value & mask);
                break;
            case Operation.Set:
                change = current => current | (value & mask);
                break;
            default:
                change = current => current & ~(value & mask);
                break;
            }

            switch(register)
            {
            case GpioOutputs:
                sio.UpdateGpioOutputs(change);
                return true;
            case GpioOutputEnables:
                sio.UpdateGpioOutputEnables(change);
                return true;
            default:
                return false;
            }
        }

        private bool ExecuteDouble(Instruction instruction)
        {
            // Read the status and clear the engaged flag. Nothing ever
            // engages the coprocessor here.
            if(instruction.Kind == InstructionKind.MoveFromCoprocessor && instruction.Opc1 == 0
                && instruction.CRn == 0 && instruction.CRm == 0 && instruction.Opc2 == 1)
            {
                WriteResult(instruction.Rt, 0);
                return true;
            }

            ClearResults(instruction);
            return false;
        }

        private bool ExecuteRedundancy(Instruction instruction)
        {
            var core = MultiprocessingId & 1;

            // Give the canary salt of a core its value. Software may only do
            // this once, and asks first.
            if(instruction.Kind == InstructionKind.MoveToCoprocessor64 && instruction.Opc1 == 8 && instruction.CRm < 2)
            {
                saltValid[instruction.CRm] = true;
                return true;
            }

            // Has the salt of this core been given a value? The answer comes
            // in the N flag.
            if(instruction.Kind == InstructionKind.MoveFromCoprocessor && instruction.Opc1 == 1
                && instruction.CRn == 0 && instruction.CRm == 0 && instruction.Opc2 == 0)
            {
                WriteResult(instruction.Rt, saltValid[core] ? 0x80000000 : 0);
                return true;
            }

            // Everything else is a check that passes, or reads as 0.
            ClearResults(instruction);
            return true;
        }

        private void ClearResults(Instruction instruction)
        {
            switch(instruction.Kind)
            {
            case InstructionKind.MoveFromCoprocessor:
                WriteResult(instruction.Rt, 0);
                break;
            case InstructionKind.MoveFromCoprocessor64:
                SetRegister((int)instruction.Rt, 0);
                SetRegister((int)instruction.Rt2, 0);
                break;
            }
        }

        // A move from a coprocessor to "register 15" sets the flags from the
        // top four bits of the value instead.
        private void WriteResult(uint register, uint value)
        {
            if(register == 15)
            {
                CPSR = (CPSR.RawValue & ~FlagsMask) | (value & FlagsMask);
            }
            else
            {
                SetRegister((int)register, value);
            }
        }

        // ITSTATE sits in two pieces in the xPSR.
        private uint ItState => (uint)(((CPSR.RawValue >> 25) & 0x3) | ((CPSR.RawValue >> 8) & 0xFC));

        // Inside an IT block the instruction only happens if its condition
        // holds; it is stepped over either way.
        private bool ConditionPasses()
        {
            var it = ItState;
            return (it & 0xF) == 0 || EvaluateConditionCode(it >> 4);
        }

        private void AdvanceItState()
        {
            var it = ItState;
            if((it & 0xF) == 0)
            {
                return;
            }
            it = (it & 0x7) == 0 ? 0 : (it & 0xE0) | ((it << 1) & 0x1F);
            CPSR = (CPSR.RawValue & ~ItStateMask) | ((ulong)(it & 0x3) << 25) | ((ulong)(it & 0xFC) << 8);
        }

        private uint InstructionSize(ulong address)
        {
            var first = machine.SystemBus.ReadWord(address, this);
            return (first & 0xE000) == 0xE000 && (first & 0x1800) != 0 ? 4u : 2u;
        }

        // Thumb encodings of the coprocessor instructions, condition "always".
        private bool TryDecode(ulong address, out Instruction instruction)
        {
            var first = (uint)machine.SystemBus.ReadWord(address, this);
            instruction = default(Instruction);
            if((first & 0xFE00) != 0xEE00 && (first & 0xFE00) != 0xEC00)
            {
                return false;
            }

            var second = (uint)machine.SystemBus.ReadWord(address + 2, this);
            instruction.Address = address;
            instruction.Encoding = (first << 16) | second;
            instruction.Coprocessor = (second >> 8) & 0xF;
            instruction.Rt = second >> 12;
            instruction.CRm = second & 0xF;

            if((first & 0xFF00) == 0xEE00)
            {
                // MCR, MRC and CDP
                instruction.CRn = first & 0xF;
                instruction.Opc2 = (second >> 5) & 0x7;
                if((second & 0x10) == 0)
                {
                    instruction.Kind = InstructionKind.DataProcessing;
                    instruction.Opc1 = (first >> 4) & 0xF;
                }
                else
                {
                    instruction.Kind = (first & 0x10) != 0 ? InstructionKind.MoveFromCoprocessor : InstructionKind.MoveToCoprocessor;
                    instruction.Opc1 = (first >> 5) & 0x7;
                }
            }
            else if((first & 0xFFE0) == 0xEC40)
            {
                // MCRR and MRRC
                instruction.Kind = (first & 0x10) != 0 ? InstructionKind.MoveFromCoprocessor64 : InstructionKind.MoveToCoprocessor64;
                instruction.Rt2 = first & 0xF;
                instruction.Opc1 = (second >> 4) & 0xF;
            }
            else
            {
                // LDC and STC
                instruction.Kind = InstructionKind.LoadStore;
            }
            return true;
        }

        private static bool IsModelled(uint coprocessor)
        {
            return coprocessor == GpioCoprocessor || coprocessor == DoubleCoprocessor || coprocessor == RedundancyCoprocessor;
        }

        private readonly RP2350_SIO sio;
        private readonly Action<ICpuSupportingGdb, ulong> coprocessorHook;
        private readonly bool[] saltValid = new bool[2];
        private readonly HashSet<uint> reported = new HashSet<uint>();

        private static readonly Regex UnsupportedAccess = new Regex(@"unsupported AArch32 \d+ bit system register cp:(\d+)", RegexOptions.Compiled);

        private const uint GpioCoprocessor = 0;
        private const uint DoubleCoprocessor = 4;
        private const uint RedundancyCoprocessor = 7;

        private const uint GpioOutputs = 0;
        private const uint GpioOutputEnables = 4;
        private const uint GpioInputs = 8;

        private const ulong MaximumBlockBytes = 0x2000;
        private const ulong FlagsMask = 0xF0000000;
        private const ulong ItStateMask = 0x0600FC00;

        private enum Operation
        {
            Put = 0,
            Xor = 1,
            Set = 2,
            Clear = 3,
        }

        private enum InstructionKind
        {
            MoveToCoprocessor,
            MoveFromCoprocessor,
            MoveToCoprocessor64,
            MoveFromCoprocessor64,
            DataProcessing,
            LoadStore,
        }

        private struct Instruction
        {
            public ulong Address;
            public uint Encoding;
            public InstructionKind Kind;
            public uint Coprocessor;
            public uint Opc1;
            public uint Opc2;
            public uint CRn;
            public uint CRm;
            public uint Rt;
            public uint Rt2;
        }
    }
}
