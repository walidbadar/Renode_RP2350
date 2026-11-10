..
   Copyright (c) 2026 Muhammad Waleed Badar
   SPDX-License-Identifier: Apache-2.0

What is modelled
################

``platforms/rp2350.repl`` describes the chip. The models named ``RP2350_*``
are the C# files in ``peripherals/``; the rest comes with Renode.

Processors and memory
*********************

.. list-table::
   :header-rows: 1
   :widths: 22 22 56

   * - Part
     - Address
     - Model
   * - Core 0, core 1
     -
     - ``RP2350_CortexM33``: Renode's Cortex-M33 with TrustZone, 8 MPU and
       8 SAU regions, plus the coprocessors and ``WFE`` events described in
       :doc:`dual_core`
   * - NVIC, SysTick
     - ``0xe000e000``
     - Renode's, one for each core
   * - Boot ROM
     - ``0x00000000``
     - 32 kB of memory holding the stand-in from ``bootrom/``
   * - Flash
     - ``0x10000000``
     - 16 MB of memory at the XIP window. Images are loaded straight into
       it; there is no flash chip behind it.
   * - SRAM
     - ``0x20000000``
     - 520 kB of memory
   * - BOOTRAM
     - ``0x400e0000``
     - ``RP2350_BootRam``: the ROM's 1 kB of RAM, the two write-once
       registers and the eight boot locks

Peripherals
***********

.. list-table::
   :header-rows: 1
   :widths: 18 18 64

   * - Block
     - Address
     - Model
   * - SIO
     - ``0xd0000000``
     - ``RP2350_SIO``: CPUID, mailboxes, doorbells, spinlocks, GPIO
   * - PSM
     - ``0x40018000``
     - ``RP2350_PSM``: starts both cores in the ROM; ``FRCE_OFF.PROC1``
       resets core 1; the other stages are stored
   * - RESETS
     - ``0x40020000``
     - ``RP2350_Resets``: putting a block into reset resets the model at
       its address; blocks come out of reset at once
   * - CLOCKS
     - ``0x40010000``
     - ``RP2350_Clocks``: every clock generator, with source selection and
       dividers; the frequency counter; SysTick of both cores follows
       ``clk_sys``
   * - XOSC
     - ``0x40048000``
     - ``RP2350_XOSC``: 12 MHz, stable as soon as it is enabled
   * - ROSC
     - ``0x400e8000``
     - ``RP2350_ROSC``: a fixed 11 MHz, whatever range and divider are
       programmed
   * - PLL_SYS, PLL_USB
     - ``0x40050000``, ``0x40058000``
     - ``RP2350_PLL``: locks at once when powered with a running
       reference; the output follows the dividers
   * - TICKS
     - ``0x40108000``
     - ``RP2350_Ticks``: the tick generators that the timers and the
       watchdog count
   * - TIMER0, TIMER1
     - ``0x400b0000``, ``0x400b8000``
     - ``RP2350_Timer``: 64-bit counter, four alarms each with an
       interrupt, pause, and either count source
   * - WATCHDOG
     - ``0x400d8000``
     - ``RP2350_Watchdog``: countdown and forced reset of the machine;
       the scratch registers and the reason survive it
   * - IO_BANK0
     - ``0x40028000``
     - ``RP2350_IOBank0``: function select, the four overrides, and edge
       and level interrupts for each core. The pins are here: connect LEDs
       and buttons to it.
   * - PADS_BANK0
     - ``0x40038000``
     - ``RP2350_Pads``: input enable and isolation act on the pins; the
       rest is stored
   * - UART0, UART1
     - ``0x40070000``, ``0x40078000``
     - Renode's PL011, with ``RP2350_AtomicAlias`` in front of it
   * - SYSINFO
     - ``0x40000000``
     - ``RP2350_SysInfo``: an RP2350A at revision A2
   * - TBMAN
     - ``0x40160000``
     - ``RP2350_TBMAN``: says the platform is silicon

Every block behind the APB and AHB bridges has, besides its registers, the
three aliases that XOR, set and clear bits on write. The models in this
repository decode them themselves; ``RP2350_AtomicAlias`` adds them to a
model that does not.

Clocks
======

The clock tree is followed far enough to know the frequency of every clock.
That is what keeps time honest when firmware reprograms it:

- SysTick on both cores counts at ``clk_sys``.
- The tick generators divide ``clk_ref``; a timer or the watchdog whose
  generator is disabled, or has the wrong cycle count, stands still or runs
  at the wrong rate, as on the chip.
- The frequency counter reports the frequency the model has for the source
  it is pointed at.

Switching a source takes no time. The clock inputs on GPIO pins count as
stopped.

Not modelled
************

These blocks have a name in the platform description, so that the log says
which one an access went to. Reads return 0 and writes are dropped:

SYSCFG, IO_QSPI, PADS_QSPI, ACCESSCTRL, BUSCTRL, SPI0, SPI1, I2C0, I2C1,
ADC, PWM, HSTX, XIP_CTRL, QMI, TRNG, SHA256, POWMAN, OTP, CoreSight, the
glitch detector, DMA, USB, PIO0 to PIO2, XIP_AUX, the Non-secure SIO and
the extended PPB.

The two RISC-V cores of the chip are not modelled either.

Parameters
**********

Set in the platform description:

.. list-table::
   :header-rows: 1
   :widths: 30 20 50

   * - Parameter
     - Default
     - Meaning
   * - ``xosc: frequency``
     - 12000000
     - Crystal frequency in Hz
   * - ``rosc: frequency``
     - 11000000
     - Frequency the ring oscillator is taken to run at, in Hz
   * - ``sysinfo: revision``
     - 2
     - Silicon revision reported in ``CHIP_ID``; 2 is A2
   * - ``sysinfo: qfn60``
     - true
     - RP2350A; false for the 80-pin RP2350B
   * - ``cpu0``, ``cpu1``: ``wfeEventPeriod``
     - 1000
     - Microseconds after which a core sleeping in ``WFE`` gets an event;
       0 for never. See :doc:`dual_core`.
