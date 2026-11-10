//
// Copyright (c) 2026 Muhammad Waleed Badar
// SPDX-License-Identifier: GPL-3.0-only
//
using System;
using Antmicro.Renode.Core;
using Antmicro.Renode.Core.Structure.Registers;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.IRQControllers;

namespace Antmicro.Renode.Peripherals.Miscellaneous
{
    // XOSC: the crystal oscillator. It is stable as soon as it is enabled.
    public class RP2350_XOSC : RP2350_RegisterBlock
    {
        public RP2350_XOSC(IMachine machine, long frequency = 12000000) : base(machine)
        {
            crystalFrequency = frequency;

            Registers.Control.Define(this)
                .WithValueField(0, 12, name: "FREQ_RANGE")
                .WithValueField(12, 12, out enable, name: "ENABLE")
                .WithReservedBits(24, 8)
                .WithChangeCallback((_, __) => FrequencyChanged?.Invoke());

            Registers.Status.Define(this)
                .WithValueField(0, 2, FieldMode.Read, name: "FREQ_RANGE")
                .WithReservedBits(2, 10)
                .WithFlag(12, FieldMode.Read, valueProviderCallback: _ => IsEnabled, name: "ENABLED")
                .WithReservedBits(13, 11)
                .WithFlag(24, FieldMode.Read | FieldMode.WriteOneToClear, name: "BADWRITE")
                .WithReservedBits(25, 6)
                .WithFlag(31, FieldMode.Read, valueProviderCallback: _ => IsEnabled, name: "STABLE");
            MarkWriteOneToClear((long)Registers.Status);

            Registers.Dormant.Define(this, DormantWake)
                .WithValueField(0, 32, name: "DORMANT", writeCallback: (_, value) =>
                {
                    if(value == DormantComa)
                    {
                        this.Log(LogLevel.Warning, "Dormant mode was requested, which is not modelled; the oscillator keeps running");
                    }
                });

            Registers.Startup.Define(this)
                .WithValueField(0, 14, name: "DELAY")
                .WithReservedBits(14, 6)
                .WithFlag(20, name: "X4")
                .WithReservedBits(21, 11);

            // A down counter for short software delays. It has always
            // finished by the time it is read back.
            Registers.Count.Define(this)
                .WithValueField(0, 16, valueProviderCallback: _ => 0, name: "COUNT")
                .WithReservedBits(16, 16);
        }

        public override void Reset()
        {
            base.Reset();
            FrequencyChanged?.Invoke();
        }

        // Output frequency in Hz, 0 while the oscillator is off.
        public long Frequency => IsEnabled ? crystalFrequency : 0;

        public event Action FrequencyChanged;

        private bool IsEnabled => enable.Value == EnableMagic;

        private readonly long crystalFrequency;
        private readonly IValueRegisterField enable;

        private const uint EnableMagic = 0xFAB;
        private const uint DormantWake = 0x77616B65;
        private const uint DormantComa = 0x636F6D61;

        private enum Registers : long
        {
            Control = 0x00,
            Status = 0x04,
            Dormant = 0x08,
            Startup = 0x0C,
            Count = 0x10,
        }
    }

    // ROSC: the ring oscillator the chip boots from. A ring oscillator has
    // no exact frequency; this one runs at a fixed nominal one, whatever
    // range, drive strength and divider are programmed.
    public class RP2350_ROSC : RP2350_RegisterBlock
    {
        public RP2350_ROSC(IMachine machine, long frequency = 11000000) : base(machine)
        {
            nominalFrequency = frequency;

            Registers.Control.Define(this, 0xAA0)
                .WithValueField(0, 12, name: "FREQ_RANGE")
                .WithValueField(12, 12, out enable, name: "ENABLE")
                .WithReservedBits(24, 8)
                .WithChangeCallback((_, __) => FrequencyChanged?.Invoke());

            Registers.FrequencyA.Define(this, 0x88)
                .WithValueField(0, 32, name: "FREQA");
            Registers.FrequencyB.Define(this)
                .WithValueField(0, 32, name: "FREQB");
            Registers.Random.Define(this, 0x3F04B16D)
                .WithValueField(0, 32, name: "RANDOM");
            Registers.Dormant.Define(this, DormantWake)
                .WithValueField(0, 32, name: "DORMANT", writeCallback: (_, value) =>
                {
                    if(value == DormantComa)
                    {
                        this.Log(LogLevel.Warning, "Dormant mode was requested, which is not modelled; the oscillator keeps running");
                    }
                });
            Registers.Divider.Define(this)
                .WithValueField(0, 16, name: "DIV")
                .WithReservedBits(16, 16);
            Registers.Phase.Define(this, 0x8)
                .WithValueField(0, 12, name: "PHASE")
                .WithReservedBits(12, 20);

            Registers.Status.Define(this)
                .WithReservedBits(0, 12)
                .WithFlag(12, FieldMode.Read, valueProviderCallback: _ => IsEnabled, name: "ENABLED")
                .WithReservedBits(13, 3)
                .WithFlag(16, FieldMode.Read, valueProviderCallback: _ => IsEnabled, name: "DIV_RUNNING")
                .WithReservedBits(17, 7)
                .WithFlag(24, FieldMode.Read | FieldMode.WriteOneToClear, name: "BADWRITE")
                .WithReservedBits(25, 6)
                .WithFlag(31, FieldMode.Read, valueProviderCallback: _ => IsEnabled, name: "STABLE");
            MarkWriteOneToClear((long)Registers.Status);

            Registers.RandomBit.Define(this)
                .WithFlag(0, FieldMode.Read, valueProviderCallback: _ => random.Next(2) != 0, name: "RANDOMBIT")
                .WithReservedBits(1, 31);

            Registers.Count.Define(this)
                .WithValueField(0, 16, valueProviderCallback: _ => 0, name: "COUNT")
                .WithReservedBits(16, 16);
        }

        public override void Reset()
        {
            base.Reset();
            FrequencyChanged?.Invoke();
        }

        // Output frequency in Hz, 0 while the oscillator is off.
        public long Frequency => IsEnabled ? nominalFrequency : 0;

        public event Action FrequencyChanged;

        // Any value but the disable code leaves it running, the reset value
        // included.
        private bool IsEnabled => enable.Value != DisableMagic;

        private readonly long nominalFrequency;
        private readonly IValueRegisterField enable;
        private readonly PseudorandomNumberGenerator random = EmulationManager.Instance.CurrentEmulation.RandomGenerator;

        private const uint DisableMagic = 0xD1E;
        private const uint DormantWake = 0x77616B65;
        private const uint DormantComa = 0x636F6D61;

        private enum Registers : long
        {
            Control = 0x00,
            FrequencyA = 0x04,
            FrequencyB = 0x08,
            Random = 0x0C,
            Dormant = 0x10,
            Divider = 0x14,
            Phase = 0x18,
            Status = 0x1C,
            RandomBit = 0x20,
            Count = 0x24,
        }
    }

    // PLL_SYS and PLL_USB. A PLL locks as soon as it is powered with a
    // feedback divider and a running reference, and never loses lock.
    public class RP2350_PLL : RP2350_RegisterBlock
    {
        public RP2350_PLL(IMachine machine, RP2350_XOSC reference) : base(machine)
        {
            this.reference = reference;
            reference.FrequencyChanged += () => FrequencyChanged?.Invoke();

            Registers.ControlStatus.Define(this, 0x1)
                .WithValueField(0, 6, out referenceDivider, name: "REFDIV")
                .WithReservedBits(6, 2)
                .WithFlag(8, out bypass, name: "BYPASS")
                .WithReservedBits(9, 21)
                .WithFlag(30, FieldMode.Read, name: "LOCK_N")
                .WithFlag(31, FieldMode.Read, valueProviderCallback: _ => IsLocked, name: "LOCK")
                .WithChangeCallback((_, __) => FrequencyChanged?.Invoke());

            Registers.Power.Define(this, 0x2D)
                .WithFlag(0, out powerDown, name: "PD")
                .WithReservedBits(1, 1)
                .WithFlag(2, name: "DSMPD")
                .WithFlag(3, out postDividerPowerDown, name: "POSTDIVPD")
                .WithReservedBits(4, 1)
                .WithFlag(5, out vcoPowerDown, name: "VCOPD")
                .WithReservedBits(6, 26)
                .WithChangeCallback((_, __) => FrequencyChanged?.Invoke());

            Registers.FeedbackDivider.Define(this)
                .WithValueField(0, 12, out feedbackDivider, name: "FBDIV_INT")
                .WithReservedBits(12, 20)
                .WithChangeCallback((_, __) => FrequencyChanged?.Invoke());

            Registers.PostDividers.Define(this, 0x77000)
                .WithReservedBits(0, 12)
                .WithValueField(12, 3, out postDivider2, name: "POSTDIV2")
                .WithReservedBits(15, 1)
                .WithValueField(16, 3, out postDivider1, name: "POSTDIV1")
                .WithReservedBits(19, 13)
                .WithChangeCallback((_, __) => FrequencyChanged?.Invoke());

            // Loss-of-lock interrupt: it never fires, the registers are kept.
            Registers.RawInterrupts.Define(this)
                .WithFlag(0, FieldMode.Read | FieldMode.WriteOneToClear, name: "LOCK_N_STICKY")
                .WithReservedBits(1, 31);
            MarkWriteOneToClear((long)Registers.RawInterrupts);
            Registers.InterruptEnable.Define(this)
                .WithFlag(0, name: "LOCK_N_STICKY")
                .WithReservedBits(1, 31);
            Registers.InterruptForce.Define(this)
                .WithFlag(0, name: "LOCK_N_STICKY")
                .WithReservedBits(1, 31);
            Registers.InterruptStatus.Define(this)
                .WithFlag(0, FieldMode.Read, name: "LOCK_N_STICKY")
                .WithReservedBits(1, 31);
        }

        public override void Reset()
        {
            base.Reset();
            FrequencyChanged?.Invoke();
        }

        // Output frequency in Hz, 0 while the PLL has no usable output.
        public long Frequency
        {
            get
            {
                if(bypass.Value)
                {
                    return reference.Frequency;
                }
                if(!IsLocked || postDividerPowerDown.Value)
                {
                    return 0;
                }
                var vco = reference.Frequency / (long)referenceDivider.Value * (long)feedbackDivider.Value;
                return vco / (long)(Math.Max(postDivider1.Value, 1) * Math.Max(postDivider2.Value, 1));
            }
        }

        public event Action FrequencyChanged;

        private bool IsLocked => !powerDown.Value && !vcoPowerDown.Value
            && feedbackDivider.Value != 0 && referenceDivider.Value != 0 && reference.Frequency != 0;

        private readonly RP2350_XOSC reference;
        private readonly IValueRegisterField referenceDivider;
        private readonly IValueRegisterField feedbackDivider;
        private readonly IValueRegisterField postDivider1;
        private readonly IValueRegisterField postDivider2;
        private readonly IFlagRegisterField bypass;
        private readonly IFlagRegisterField powerDown;
        private readonly IFlagRegisterField postDividerPowerDown;
        private readonly IFlagRegisterField vcoPowerDown;

        private enum Registers : long
        {
            ControlStatus = 0x00,
            Power = 0x04,
            FeedbackDivider = 0x08,
            PostDividers = 0x0C,
            RawInterrupts = 0x10,
            InterruptEnable = 0x14,
            InterruptForce = 0x18,
            InterruptStatus = 0x1C,
        }
    }

    // CLOCKS: the clock generators. The model follows the clock tree far
    // enough to know the frequency of every clock, which is what keeps the
    // SysTick of both cores, the tick generators and the frequency counter
    // honest when firmware reprograms the PLLs or the dividers. Switching a
    // clock source is instant, and the glitchless multiplexers report the new
    // source at once.
    //
    // The clock inputs on GPIO pins are not modelled and count as stopped.
    // Resuscitation, clock gating in sleep and the clock interrupts are
    // stored only.
    public class RP2350_Clocks : RP2350_RegisterBlock
    {
        public RP2350_Clocks(IMachine machine, RP2350_XOSC xosc, RP2350_ROSC rosc, RP2350_PLL pllSys, RP2350_PLL pllUsb) : base(machine)
        {
            this.xosc = xosc;
            this.rosc = rosc;
            this.pllSys = pllSys;
            this.pllUsb = pllUsb;

            xosc.FrequencyChanged += SourceChanged;
            rosc.FrequencyChanged += SourceChanged;
            pllSys.FrequencyChanged += SourceChanged;
            pllUsb.FrequencyChanged += SourceChanged;

            for(var index = 0; index < NumberOfClocks; index++)
            {
                DefineClock((Clock)index);
            }

            Registers.ResusControl.Define(this, 0xFF)
                .WithValueField(0, 8, name: "TIMEOUT")
                .WithFlag(8, name: "ENABLE")
                .WithReservedBits(9, 3)
                .WithFlag(12, name: "FRCE")
                .WithReservedBits(13, 3)
                .WithFlag(16, FieldMode.Write, name: "CLEAR")
                .WithReservedBits(17, 15);
            Registers.ResusStatus.Define(this)
                .WithFlag(0, FieldMode.Read, name: "RESUSSED")
                .WithReservedBits(1, 31);

            Registers.Fc0ReferenceKhz.Define(this)
                .WithValueField(0, 20, name: "FC0_REF_KHZ")
                .WithReservedBits(20, 12);
            Registers.Fc0MinKhz.Define(this)
                .WithValueField(0, 25, name: "FC0_MIN_KHZ")
                .WithReservedBits(25, 7);
            Registers.Fc0MaxKhz.Define(this, 0x1FFFFFF)
                .WithValueField(0, 25, name: "FC0_MAX_KHZ")
                .WithReservedBits(25, 7);
            Registers.Fc0Delay.Define(this, 0x1)
                .WithValueField(0, 3, name: "FC0_DELAY")
                .WithReservedBits(3, 29);
            Registers.Fc0Interval.Define(this, 0x8)
                .WithValueField(0, 4, name: "FC0_INTERVAL")
                .WithReservedBits(4, 28);
            // Writing the source starts a measurement, which finishes at once.
            Registers.Fc0Source.Define(this)
                .WithValueField(0, 8, out counterSource, name: "FC0_SRC")
                .WithReservedBits(8, 24);
            Registers.Fc0Status.Define(this)
                .WithFlag(0, FieldMode.Read, valueProviderCallback: _ => counterSource.Value != 0, name: "PASS")
                .WithReservedBits(1, 3)
                .WithFlag(4, FieldMode.Read, valueProviderCallback: _ => counterSource.Value != 0, name: "DONE")
                .WithReservedBits(5, 27);
            Registers.Fc0Result.Define(this)
                .WithValueField(0, 5, FieldMode.Read,
                    valueProviderCallback: _ => (ulong)(CounterFrequency % 1000 * 32 / 1000), name: "FRAC")
                .WithValueField(5, 25, FieldMode.Read,
                    valueProviderCallback: _ => (ulong)(CounterFrequency / 1000), name: "KHZ")
                .WithReservedBits(30, 2);

            Registers.WakeEnable0.Define(this, 0xFFFFFFFF)
                .WithValueField(0, 32, name: "WAKE_EN0");
            Registers.WakeEnable1.Define(this, 0x7FFFFFFF)
                .WithValueField(0, 31, name: "WAKE_EN1")
                .WithReservedBits(31, 1);
            Registers.SleepEnable0.Define(this, 0xFFFFFFFF)
                .WithValueField(0, 32, name: "SLEEP_EN0");
            Registers.SleepEnable1.Define(this, 0x7FFFFFFF)
                .WithValueField(0, 31, name: "SLEEP_EN1")
                .WithReservedBits(31, 1);
            Registers.Enabled0.Define(this)
                .WithValueField(0, 32, FieldMode.Read, valueProviderCallback: _ => 0xFFFFFFFF, name: "ENABLED0");
            Registers.Enabled1.Define(this)
                .WithValueField(0, 31, FieldMode.Read, valueProviderCallback: _ => 0x7FFFFFFF, name: "ENABLED1")
                .WithReservedBits(31, 1);

            Registers.RawInterrupts.Define(this)
                .WithFlag(0, FieldMode.Read, name: "CLK_SYS_RESUS")
                .WithReservedBits(1, 31);
            Registers.InterruptEnable.Define(this)
                .WithFlag(0, name: "CLK_SYS_RESUS")
                .WithReservedBits(1, 31);
            Registers.InterruptForce.Define(this)
                .WithFlag(0, name: "CLK_SYS_RESUS")
                .WithReservedBits(1, 31);
            Registers.InterruptStatus.Define(this)
                .WithFlag(0, FieldMode.Read, name: "CLK_SYS_RESUS")
                .WithReservedBits(1, 31);
        }

        public override void Reset()
        {
            base.Reset();
            Update();
        }

        // clk_ref in Hz: the tick generators divide this down.
        public long ReferenceFrequency => GetFrequency(Clock.Ref);

        // clk_sys in Hz: the processors and their SysTick run from this.
        public long SystemFrequency => GetFrequency(Clock.Sys);

        // clk_peri in Hz: the UARTs and SPIs run from this.
        public long PeripheralFrequency => GetFrequency(Clock.Peri);

        // Raised whenever a clock may have changed frequency.
        public event Action FrequenciesChanged;

        private void DefineClock(Clock clock)
        {
            var offset = (long)clock * ClockStride;
            var hasEnable = clock != Clock.Ref && clock != Clock.Sys;

            var controlRegister = RegistersCollection.DefineRegister(offset, clock == Clock.Sys ? 0x41u : 0u)
                .WithValueField(0, 28, out control[(int)clock], name: "CTRL")
                .WithReservedBits(29, 3)
                .WithChangeCallback((_, __) => Update());
            if(hasEnable)
            {
                controlRegister.WithFlag(28, FieldMode.Read,
                    valueProviderCallback: _ => (control[(int)clock].Value & ControlEnable) != 0, name: "ENABLED");
            }
            else
            {
                controlRegister.WithReservedBits(28, 1);
            }

            RegistersCollection.DefineRegister(offset + 0x4, 0x10000)
                .WithValueField(0, 32, out divider[(int)clock], name: "DIV")
                .WithChangeCallback((_, __) => Update());

            // One-hot: which input of the glitchless multiplexer is in use.
            // Clocks without such a multiplexer always read 1.
            RegistersCollection.DefineRegister(offset + 0x8)
                .WithValueField(0, 4, FieldMode.Read, valueProviderCallback: _ =>
                {
                    switch(clock)
                    {
                    case Clock.Ref:
                        return 1UL << (int)(control[(int)clock].Value & 0x3);
                    case Clock.Sys:
                        return 1UL << (int)(control[(int)clock].Value & 0x1);
                    default:
                        return 1;
                    }
                }, name: "SELECTED")
                .WithReservedBits(4, 28);
        }

        private long GetFrequency(Clock clock)
        {
            var ctrl = (uint)control[(int)clock].Value;
            var div = (uint)divider[(int)clock].Value;
            var auxiliary = (ctrl >> 5) & 0xF;
            long source;

            switch(clock)
            {
            case Clock.Ref:
                switch(ctrl & 0x3)
                {
                case 0:
                    source = rosc.Frequency;
                    break;
                case 1:
                    source = (auxiliary & 0x3) == 0 ? pllUsb.Frequency : 0;
                    break;
                case 2:
                    source = xosc.Frequency;
                    break;
                default:
                    source = LowPowerOscillatorFrequency;
                    break;
                }
                return Divide(source, div, 8, fractional: false);

            case Clock.Sys:
                if((ctrl & 0x1) == 0)
                {
                    source = GetFrequency(Clock.Ref);
                }
                else
                {
                    source = Select(auxiliary & 0x7, pllSys.Frequency, pllUsb.Frequency, rosc.Frequency, xosc.Frequency);
                }
                return Divide(source, div, 16, fractional: true);

            case Clock.Peri:
                source = Select(auxiliary & 0x7, GetFrequency(Clock.Sys), pllSys.Frequency, pllUsb.Frequency, rosc.Frequency, xosc.Frequency);
                return IsRunning(ctrl) ? Divide(source, div, 2, fractional: false) : 0;

            case Clock.Hstx:
                source = Select(auxiliary & 0x7, GetFrequency(Clock.Sys), pllSys.Frequency, pllUsb.Frequency);
                return IsRunning(ctrl) ? Divide(source, div, 2, fractional: false) : 0;

            case Clock.Usb:
            case Clock.Adc:
                source = Select(auxiliary & 0x7, pllUsb.Frequency, pllSys.Frequency, rosc.Frequency, xosc.Frequency);
                return IsRunning(ctrl) ? Divide(source, div, 4, fractional: false) : 0;

            default:
                // The four clock outputs on GPIO pins.
                source = Select(auxiliary, pllSys.Frequency, 0, 0, pllUsb.Frequency, 0, rosc.Frequency, xosc.Frequency,
                    LowPowerOscillatorFrequency, GetFrequency(Clock.Sys), GetFrequency(Clock.Usb), GetFrequency(Clock.Adc),
                    GetFrequency(Clock.Ref), GetFrequency(Clock.Peri), GetFrequency(Clock.Hstx));
                return IsRunning(ctrl) ? Divide(source, div, 16, fractional: true) : 0;
            }
        }

        // What the frequency counter is pointed at, in Hz.
        private long CounterFrequency
        {
            get
            {
                return Select((uint)counterSource.Value, 0, pllSys.Frequency, pllUsb.Frequency, rosc.Frequency, rosc.Frequency,
                    xosc.Frequency, 0, 0, GetFrequency(Clock.Ref), GetFrequency(Clock.Sys), GetFrequency(Clock.Peri),
                    GetFrequency(Clock.Usb), GetFrequency(Clock.Adc), GetFrequency(Clock.Hstx), LowPowerOscillatorFrequency);
            }
        }

        private void SourceChanged()
        {
            lock(locker)
            {
                Update();
            }
        }

        private void Update()
        {
            // SysTick counts processor clock cycles on both cores.
            var system = SystemFrequency;
            if(system > 0)
            {
                foreach(var nvic in machine.GetPeripheralsOfType<NVIC>())
                {
                    nvic.Frequency = system;
                }
            }
            FrequenciesChanged?.Invoke();
        }

        private static bool IsRunning(uint ctrl)
        {
            return (ctrl & ControlEnable) != 0 && (ctrl & ControlKill) == 0;
        }

        private static long Select(uint index, params long[] sources)
        {
            return index < sources.Length ? sources[index] : 0;
        }

        // The dividers hold an integer part above bit 16 and, on some clocks,
        // a 16-bit fraction below it. An integer part of 0 means the largest
        // divisor, one more than the field can hold.
        private static long Divide(long source, uint div, int integerBits, bool fractional)
        {
            var integer = (ulong)(div >> 16) & ((1UL << integerBits) - 1);
            if(integer == 0)
            {
                integer = 1UL << integerBits;
            }
            var divisor = integer << 16;
            if(fractional)
            {
                divisor |= div & 0xFFFF;
            }
            return (long)(((ulong)source << 16) / divisor);
        }

        private readonly RP2350_XOSC xosc;
        private readonly RP2350_ROSC rosc;
        private readonly RP2350_PLL pllSys;
        private readonly RP2350_PLL pllUsb;

        private readonly IValueRegisterField[] control = new IValueRegisterField[NumberOfClocks];
        private readonly IValueRegisterField[] divider = new IValueRegisterField[NumberOfClocks];
        private readonly IValueRegisterField counterSource;

        private const int NumberOfClocks = 10;
        private const long ClockStride = 0xC;
        private const uint ControlKill = 1u << 10;
        private const uint ControlEnable = 1u << 11;
        private const long LowPowerOscillatorFrequency = 32768;

        // In register order: CTRL, DIV and SELECTED of each follow each other.
        private enum Clock
        {
            Gpout0,
            Gpout1,
            Gpout2,
            Gpout3,
            Ref,
            Sys,
            Peri,
            Hstx,
            Usb,
            Adc,
        }

        private enum Registers : long
        {
            ResusControl = 0x84,
            ResusStatus = 0x88,
            Fc0ReferenceKhz = 0x8C,
            Fc0MinKhz = 0x90,
            Fc0MaxKhz = 0x94,
            Fc0Delay = 0x98,
            Fc0Interval = 0x9C,
            Fc0Source = 0xA0,
            Fc0Status = 0xA4,
            Fc0Result = 0xA8,
            WakeEnable0 = 0xAC,
            WakeEnable1 = 0xB0,
            SleepEnable0 = 0xB4,
            SleepEnable1 = 0xB8,
            Enabled0 = 0xBC,
            Enabled1 = 0xC0,
            RawInterrupts = 0xC4,
            InterruptEnable = 0xC8,
            InterruptForce = 0xCC,
            InterruptStatus = 0xD0,
        }
    }
}
