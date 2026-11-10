..
   Copyright (c) 2026 Muhammad Waleed Badar
   SPDX-License-Identifier: Apache-2.0

Samples
#######

The same question, do two cores work together on this model, is asked
twice: on bare metal and from Zephyr. The first is this project's own
reading of the chip. The second brings code that was written against the
silicon.

None of the samples has been run on a board.

Bare metal
**********

``samples/multicore`` uses no SDK and no C library, only registers. Core 0
brings up the clocks and the UART, launches core 1 and runs eight checks;
the last line is the verdict.

The sample is not a Zephyr application. The test in ``tests/multicore``
builds it, as ``multicore.elf`` next to an image of its own:

.. code-block:: sh

   west build -b cortex_m33/rp2350a/m33_0 rp2350_renode/tests/multicore
   renode -e '$elf=@build/zephyr/multicore.elf; include @rp2350_renode/scripts/rpi_pico2.resc; start'

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

Zephyr
******

Stock samples
=============

These run from the Zephyr tree, unchanged, on the ``cortex_m33`` board:

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

   west build -b cortex_m33/rp2350a/m33_0 zephyr/samples/basic/blinky -t run

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
own, calls nothing in the kernel and takes no interrupts.

.. code-block:: sh

   west build -b cortex_m33/rp2350a/m33_0 rp2350_renode/samples/zephyr_core1 -t run
