//
// Copyright (c) 2026 Muhammad Waleed Badar
// SPDX-License-Identifier: Apache-2.0
//
using Antmicro.Renode.Core;
using Antmicro.Renode.Core.Structure.Registers;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.CPU;
using Antmicro.Renode.Peripherals.IRQControllers;

namespace Antmicro.Renode.Peripherals.Miscellaneous
{
    // PSM: the power-on state machine. Both cores start from the vector table
    // of the boot ROM at address 0, whichever ELF file was loaded last, so the
    // cores that are still in reset are pointed at the ROM whenever the machine
    // starts or resumes. FRCE_OFF.PROC1 holds core 1 in reset; releasing it
    // sends the core back through the boot ROM. The other stages are stored
    // only.
    public class RP2350_PSM : RP2350_RegisterBlock, IHasOwnLife
    {
        public RP2350_PSM(IMachine machine, CortexM core0 = null, CortexM core1 = null, NVIC nvic1 = null) : base(machine)
        {
            this.core0 = core0;
            this.core1 = core1;
            this.nvic1 = nvic1;

            Registers.ForceOn.Define(this)
                .WithValueField(0, NumberOfStages, name: "FRCE_ON")
                .WithReservedBits(NumberOfStages, 32 - NumberOfStages);

            Registers.ForceOff.Define(this)
                .WithValueField(0, NumberOfStages, out forceOff, name: "FRCE_OFF")
                .WithReservedBits(NumberOfStages, 32 - NumberOfStages)
                .WithChangeCallback((previous, current) =>
                {
                    var changed = previous ^ current;
                    if((changed & Proc1) != 0)
                    {
                        HoldCore1((current & Proc1) != 0);
                    }
                    if((current & changed & Proc0) != 0)
                    {
                        this.Log(LogLevel.Warning, "Core 0 was forced off; the model keeps it running");
                    }
                });

            Registers.WatchdogSelect.Define(this)
                .WithValueField(0, NumberOfStages, out watchdogSelect, name: "WDSEL")
                .WithReservedBits(NumberOfStages, 32 - NumberOfStages);

            Registers.Done.Define(this)
                .WithValueField(0, NumberOfStages, FieldMode.Read,
                    valueProviderCallback: _ => AllStages & ~forceOff.Value, name: "DONE")
                .WithReservedBits(NumberOfStages, 32 - NumberOfStages);
        }

        public uint WatchdogSelection => (uint)watchdogSelect.Value;

        public override void Reset()
        {
            base.Reset();
            // A machine reset ends the hold on core 1 too. The core is let
            // go when the machine starts again.
            lock(locker)
            {
                releaseCore1 = core1Held;
                core1Held = false;
            }
        }

        // The machine starts and resumes this before it does the processors.
        public void Start()
        {
            PrepareCores();
            IsPaused = false;
        }

        public void Resume()
        {
            PrepareCores();
            IsPaused = false;
        }

        public void Pause()
        {
            IsPaused = true;
        }

        public bool IsPaused { get; private set; } = true;

        private void PrepareCores()
        {
            foreach(var core in new[] { core0, core1 })
            {
                if(core == null)
                {
                    continue;
                }

                // A core that is running keeps the vector table its software
                // chose.
                if(core.EmulationState == EmulationCPUState.InReset)
                {
                    core.VectorTableOffset = RomVectorTable;
                }

                // Renode creates its per-CPU cache of bus accesses on the
                // first access of a CPU, without a lock. One access on behalf
                // of each core, made while neither is running, keeps two
                // first accesses from racing.
                machine.SystemBus.ReadDoubleWord(RomVectorTable, core, 0);
            }

            lock(locker)
            {
                if(releaseCore1)
                {
                    releaseCore1 = false;
                    core1.IsHalted = false;
                }
            }
        }

        private void HoldCore1(bool hold)
        {
            if(core1 == null)
            {
                this.Log(LogLevel.Warning, "FRCE_OFF.PROC1 was written, but the model was not given core 1 to control");
                return;
            }

            core1Held = hold;
            if(hold)
            {
                this.Log(LogLevel.Debug, "Core 1 is held in reset");
                core1.IsHalted = true;
                return;
            }

            // Out of reset the core loads its stack pointer and reset vector
            // from the ROM, like at power-on.
            this.Log(LogLevel.Debug, "Core 1 is released from reset");
            core1.Reset();
            nvic1?.Reset();
            core1.VectorTableOffset = RomVectorTable;
            core1.IsHalted = false;
        }

        private bool core1Held;
        private bool releaseCore1;

        private readonly CortexM core0;
        private readonly CortexM core1;
        private readonly NVIC nvic1;
        private readonly IValueRegisterField forceOff;
        private readonly IValueRegisterField watchdogSelect;

        private const int NumberOfStages = 25;
        private const uint AllStages = (1u << NumberOfStages) - 1;
        private const uint Proc0 = 1u << 23;
        private const uint Proc1 = 1u << 24;
        private const uint RomVectorTable = 0x00000000;

        private enum Registers : long
        {
            ForceOn = 0x0,
            ForceOff = 0x4,
            WatchdogSelect = 0x8,
            Done = 0xC,
        }
    }
}
