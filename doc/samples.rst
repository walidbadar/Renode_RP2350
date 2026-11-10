..
   Copyright (c) 2026 Muhammad Waleed Badar
   SPDX-License-Identifier: GPL-3.0-only

Samples
#######

The same question, do two cores work together on this model, is asked three
times: on bare metal, through the Pico SDK and from Zephyr. The first is
this project's own reading of the chip. The other two bring code that was
written against the silicon.

None of the samples has been run on a board.

Bare metal
**********

``samples/multicore`` uses no SDK and no C library, only registers. Core 0
brings up the clocks and the UART, launches core 1 and runs eight checks;
the last line is the verdict.

.. code-block:: sh

   make
   make run

.. list-table::
   :header-rows: 1
   :widths: 30 70

   * - Check
     - What has to work
   * - launch core 1
     - The launch protocol of the boot ROM; CPUID reads 1 on core 1
   * - mailbox ping-pong
     - Both mailboxes; the mailbox interrupt on core 1; ``WFE`` and ``SEV``
       across the cores
   * - mailbox flags
     - A depth of four; ``RDY`` and ``VLD``; the sticky ``WOF`` and
       ``ROE``; a doorbell rung on core 1
   * - spinlock
     - Mutual exclusion: both cores increment one counter 5000 times each
       and none may be lost
   * - doorbell
     - A doorbell rung on core 0, taken as an interrupt
   * - per-core SysTick
     - A SysTick for each core, at different periods, in step with the
       timer
   * - timer alarm on core 1
     - A shared interrupt line, enabled and taken on core 1 only
   * - reset and relaunch
     - ``FRCE_OFF.PROC1`` in the PSM; core 1 back in the ROM; a second
       launch

Pico SDK
********

``samples/pico_sdk`` asks again through the SDK. Start-up, clock set-up and
everything else that touches the hardware is SDK code:
``multicore_launch_core1()``, the ``multicore_fifo_*`` functions, a
``mutex_t``, the doorbell functions, ``sleep_ms()`` on both cores,
``multicore_lockout_start_blocking()`` and ``multicore_reset_core1()``.

.. code-block:: sh

   make pico-sdk PICO_SDK_PATH=/path/to/pico-sdk
   renode -e '$elf=@build/pico_sdk/sdk_multicore.elf; include @scripts/rpi_pico2.resc; start'

.. code-block:: none

   RP2350 dual-core sample, Pico SDK
   core 0: running, core number 0
   core 1: running, core number 1
   launch core 1 ............ ok
   mailbox ping-pong ........ ok
   mutex .................... ok
   doorbell ................. ok
   sleep on both cores ...... ok
   lockout .................. ok
   core 1: running, core number 1
   reset and relaunch ....... ok
   all 7 checks passed

Built with SDK 2.3.0 and the GCC of the Zephyr SDK. ``picotool`` is not
needed, because only the ELF file is used.

Zephyr
******

Stock samples
=============

``make zephyr`` builds these from the Zephyr tree, unchanged, for
``rpi_pico2/rp2350a/m33``:

.. list-table::
   :header-rows: 1
   :widths: 35 65

   * - Sample
     - What it exercises
   * - ``hello_world``
     - Clocks, resets, UART
   * - ``synchronization``
     - Threads and SysTick timing
   * - ``basic/blinky``
     - GPIO through the GPIO coprocessor, to the LED on GPIO25
   * - ``subsys/shell/shell_module``
     - UART receive interrupts
   * - ``drivers/counter/alarm``
     - TIMER0 alarms through the counter driver
   * - ``drivers/watchdog``
     - The watchdog and the reset it causes

.. code-block:: sh

   make zephyr ZEPHYR_BASE=~/zephyrproject/zephyr

The images end up in ``build/zephyr/<sample>/zephyr/zephyr.elf``.

.. _sample-zephyr-core1:

A worker on core 1
==================

Zephyr on the RP2350 runs on core 0. ``samples/zephyr_core1`` puts core 1
to work next to it: it launches a small worker there with the launch
protocol of the boot ROM and hands it numbers to square through Zephyr's
driver for the mailbox (``raspberrypi,pico-mbox``).

.. code-block:: none

   *** Booting Zephyr OS build v4.5.0 ***
   Zephyr on core 0
   worker on core 1
   1 squared is 1
   2 squared is 4
   3 squared is 9
   4 squared is 16
   5 squared is 25
   core 1 answered 5 of 5 requests

The worker is not a Zephyr thread. It has a stack and a vector table of its
own, calls nothing in the kernel and takes no interrupts. ``make zephyr``
builds it along with the stock samples.
