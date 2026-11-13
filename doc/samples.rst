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

   west build -b cortex_m33/rp2350a/m33_0 Renode_RP2350/tests/multicore
   renode -e '$elf=@build/zephyr/multicore.elf; include @Renode_RP2350/scripts/rpi_pico2.resc; start'

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

.. _zephyr-smp:

One kernel on both cores
========================

The ``smp`` variant of the board, ``cortex_m33/rp2350a/m33/smp``, builds
Zephyr with ``CONFIG_SMP``: one kernel schedules threads on both cores. Zephyr has no SMP for a Cortex-M yet. The
workspace takes it from
`pull request 124714 <https://github.com/zephyrproject-rtos/zephyr/pull/124714>`_,
a draft that adds it to the architecture code, and this repository adds
what that asks of the chip:

.. list-table::
   :header-rows: 1
   :widths: 30 70

   * - Needed
     - On the RP2350
   * - Which core is this
     - ``CPUID`` in the SIO
   * - Start core 1
     - The launch protocol of the boot ROM, through the FIFO between the
       cores, with Zephyr's reset vector as the entry point
   * - Interrupt the other core
     - Doorbell 0 of the SIO, interrupt 26 on each core
   * - Atomic operations
     - ``LDREX`` and ``STREX``, as on one core
   * - One clock for both cores
     - TIMER1, with the ``raspberrypi,pico-system-timer`` driver of Zephyr;
       SysTick is per core and is disabled

The first three are in ``soc/rp2350/smp.c``. Peripheral interrupts are
taken by core 0, the core that enables them while the drivers start; core 1
takes only the doorbell.

.. code-block:: sh

   west build -b cortex_m33/rp2350a/m33/smp zephyr/tests/kernel/multiprocessing/smp -t run

runs the SMP tests of the kernel, which among other things check that
threads really are on two cores at once.

The synchronization sample shows it more simply. With
``CONFIG_SCHED_CPU_MASK`` it pins one of its two threads to each core:

.. code-block:: sh

   west build -p -b rpi_pico2/rp2350a/m33/smp zephyr/samples/synchronization -t run -- -DCONFIG_SCHED_CPU_MASK=y

.. code-block:: none

   *** Booting Zephyr OS build c3cd7868d47c ***
   thread_a: Hello World from cpu 0 on rpi_pico2!
   thread_b: Hello World from cpu 1 on rpi_pico2!
   thread_a: Hello World from cpu 0 on rpi_pico2!
   thread_b: Hello World from cpu 1 on rpi_pico2!

Stock samples
=============

These run from the Zephyr tree, unchanged, on both variants of the
``cortex_m33`` board:

.. list-table::
   :header-rows: 1
   :widths: 35 65

   * - Sample
     - What it exercises
   * - ``hello_world``
     - Clocks, resets, UART
   * - ``synchronization``
     - Threads and the system timer
   * - ``philosophers``
     - Threads, spread over both cores on the ``smp`` variant
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

``samples/zephyr_core1`` is the other way to use core 1. On the variant
without SMP, where Zephyr keeps to core 0, it puts core 1 to work next to
it: it launches a small worker there with the launch protocol of the boot ROM and
hands it numbers to square through Zephyr's driver for the mailbox
(``raspberrypi,pico-mbox``).

.. code-block:: none

   *** Booting Zephyr OS build c3cd7868d47c ***
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

   west build -b cortex_m33/rp2350a/m33_0 Renode_RP2350/samples/zephyr_core1 -t run
