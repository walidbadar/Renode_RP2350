..
   Copyright (c) 2026 Muhammad Waleed Badar
   SPDX-License-Identifier: Apache-2.0

Limitations and troubleshooting
###############################

Tested with Renode 1.16.0 on Linux, with Zephyr at the commit that
``west.yml`` pins, which has the commits for the architecture code of
`pull request 124714 <https://github.com/zephyrproject-rtos/zephyr/pull/124714>`_.
Nothing here has been compared against a board.

What the model leaves out
*************************

- Only the blocks listed in :doc:`model` are modelled. SPI, I2C, ADC, PWM,
  PIO, DMA, USB, the QSPI interface, OTP and the rest answer with zeroes
  and a warning in the log.
- The boot ROM stand-in has four ROM functions. A lookup of any other
  returns a null pointer, and code that calls it without checking jumps to
  address 0. That includes the flash functions, so the flash drivers of the
  Pico SDK and of Zephyr do not work, and ``get_sys_info``, which Zephyr's
  hardware info driver uses.
- No secure boot, no partitions, no RISC-V cores.
- The double-precision coprocessor only answers the status read that
  start-up code makes. The Pico SDK does its ``double`` arithmetic on that
  coprocessor by default, and a program that uses ``double`` ends in the
  SDK's fault handler. Single precision uses the FPU and works. To run
  such a program, build it with the compiler's routines instead:
  ``pico_set_double_implementation(<target> compiler)``.
- The redundancy coprocessor accepts everything. Code that relies on it to
  catch a fault will not see one.
- Coprocessor instructions in the "2" encodings (``MCR2``, ``MRC2``,
  ``CDP2`` and so on) raise an undefined instruction exception. The Pico
  SDK has them in its ``double`` routines and in its macros for the
  redundancy coprocessor.
- ``CPACR`` is not checked for coprocessors 0, 4 and 7. Code that forgets to
  enable one works here and faults on the chip.
- There is no Non-secure SIO and no ACCESSCTRL: every block is reachable
  from either security state.
- A block held in reset in RESETS still answers on the bus.
- The UARTs interrupt for every received character. The FIFOs of the real
  ones are 32 deep with a timeout; Renode's PL011 has no timeout.
- Dormant mode and the sleep-enable registers of CLOCKS have no effect.

Zephyr on both cores
********************

This is about the ``smp`` variant of the board.

- SMP for Cortex-M comes from a pull request that is still a draft, and
  the code for the RP2350 in ``soc/`` has only ever run on this model.
- Core 0 takes all peripheral interrupts. A driver that enables its
  interrupt from a thread, which may be running on core 1, would enable it
  there as well, since each core has its own NVIC; the drivers used here
  enable theirs while the kernel starts, on core 0.
- No user mode, which the pull request leaves out for SMP.

What Renode does differently
****************************

- ``SLEEPONEXIT`` and ``SEVONPEND`` in the System Control Register of one
  core take effect on both. Renode's NVIC applies them to every CPU of the
  machine.
- ``ACTLR`` is not there. The Pico SDK sets ``EXTEXCLALL`` in it on each
  core, which logs ``Unhandled write to offset 0x8`` and does no harm:
  exclusive access works across the cores regardless.
- An event from the exclusive-access monitor cannot be modelled; a timed
  event stands in for it. See :doc:`dual_core`.
- A FIFO or doorbell interrupt that one core raises for the other arrives
  at the next point where Renode brings the cores into step, at most one
  quantum later. Renode's NVIC does not cope with an interrupt that
  arrives from another core's thread while its own core is raising
  ``BASEPRI``: the core then takes an exception that was never
  acknowledged, through a vector that does not exist.
- A core that sleeps in ``WFE`` notices an ``SEV`` from the other core at
  the next point where Renode brings the cores into step, at most one
  quantum later (100 µs by default).
- A core runs 100 instructions per microsecond of virtual time whatever
  the instructions are. ``cpu0 PerformanceInMips 150`` changes the figure.

Messages
********

``Method LogAsCpu is marked with Export attribute, but was not exported.``
    Expected, once for each core, when the machine is created.
    ``RP2350_CortexM33`` replaces the method through which Renode's
    translator reports instructions it cannot translate; Renode notes that
    its own is no longer the one in use.

``Changing value of the SHCSR register ..., the register isn't supported by Renode`` and ``Unhandled write to offset 0xD2C``
    Zephyr enabling the fault exceptions and clearing a fault status
    register at start-up. Harmless.

``Unhandled write to offset 0x48 ... RXDMAE ... TXDMAE``
    The Pico SDK enabling DMA requests in the UART. Harmless without a DMA
    model.

``ReadDoubleWord from non existing peripheral at ... (tag: '...')``
    The image used a block that is not modelled; the tag names it.

``Coprocessor ... instruction ... is not modelled``
    The image executed a double-precision coprocessor instruction other
    than the status read. It did nothing, and what it read was 0.

Troubleshooting
***************

Nothing on the UART, core 0 at ``rom_no_bootable_image``
    The ROM stand-in found no image definition for Arm on the RP2350 in the
    first 4 kB of flash or of SRAM. An image for the RP2040, for the RISC-V
    cores, or one without the block is not started.

Renode reports ``PC does not lay in memory or PC and SP are equal to zero`` and halts both cores
    The boot ROM is not loaded. It is built with every image for the
    ``cortex_m33`` board; check that ``$bootrom`` names it, or that
    ``rp2350_bootrom.elf`` is next to the image.

A Pico SDK program prints nothing after its first use of ``double``
    See the double-precision coprocessor above.

Both cores run the image's start-up code
    The platform description was loaded without the PSM model, or with a
    PSM that was not given the cores. Use ``platforms/rp2350.repl``.

A Pico SDK program stops in ``mutex_enter_blocking`` or in ``sleep_ms``
    The stand-in for the exclusive-access monitor event is off: check that
    ``wfeEventPeriod`` is not 0. See :doc:`dual_core`.

Short lines typed at a prompt are not seen until more is typed
    A ``fifoSize`` larger than 1 was set on a UART. Renode's PL011 only
    interrupts when the FIFO reaches its trigger level.

Renode stops with ``The given key '[CPU: ...]' was not present in the dictionary``
    A race in Renode's bus between the first accesses of two cores. The PSM
    model avoids it by making one access for each core before they start;
    a platform without that model is exposed to it.
