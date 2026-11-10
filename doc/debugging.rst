..
   Copyright (c) 2026 Muhammad Waleed Badar
   SPDX-License-Identifier: GPL-3.0-only

Debugging
#########

GDB
***

Start a GDB server from the Monitor, before or after ``start``:

.. code-block:: none

   machine StartGdbServer 3333

and attach to it. The two cores appear as two threads:

.. code-block:: none

   $ arm-zephyr-eabi-gdb build/multicore/multicore.elf
   (gdb) target remote :3333
   (gdb) info threads
     Id   Target Id                 Frame
   * 1    Thread 1 "rpi_pico2.cpu0" 0x100004e0 in check_systick () at samples/multicore/main.c:609
     2    Thread 2 "rpi_pico2.cpu1" irq_restore (primask=0) at samples/multicore/main.c:80
   (gdb) thread 2
   (gdb) bt
   #0  irq_restore (primask=0) at samples/multicore/main.c:80
   #1  core1_main () at samples/multicore/main.c:416
   #2  0x000000b4 in ?? ()

Frame 2 is the boot ROM, from where core 1 was launched. Load its symbols
to see it by name:

.. code-block:: none

   (gdb) add-symbol-file build/bootrom/bootrom.elf

Monitor commands work from GDB with ``monitor``, for example
``monitor cpu1 PC``.

Where each core is
******************

.. code-block:: none

   (monitor) cpu0 PC
   (monitor) cpu1 PC
   (monitor) cpu1 IsHalted

A core 1 that was never launched sits in the ROM, below ``0x8000``, asleep
in ``WFE``. ``IsHalted`` is true only while software holds the core in
reset through the PSM. Core 0 at ``rom_no_bootable_image`` means the ROM
found no image it could start; see :ref:`boot-rom`.

The mailbox from outside
************************

The Monitor reads and writes the SIO as core 0 would. Reading ``FIFO_RD``
takes a word out of the mailbox like any other read:

.. code-block:: none

   (monitor) sysbus ReadDoubleWord 0xD0000050
   (monitor) sysbus ReadDoubleWord 0xD0000058

The four interrupt lines of the SIO can be looked at directly:

.. code-block:: none

   (monitor) sio FifoIRQ0 IsSet
   (monitor) sio DoorbellIRQ1 IsSet

Logs
****

.. code-block:: none

   (monitor) logLevel -1 sio
   (monitor) logLevel -1 psm
   (monitor) sysbus LogPeripheralAccess sio true
   (monitor) cpu1 LogFunctionNames true

``LogPeripheralAccess`` prints every register access with the core it came
from. An access to a block that is not modelled is logged at warning level
with the name of the block.

Repeatable runs
***************

With the cores in threads of their own, two runs do not interleave the same
way. To make a run repeat exactly:

.. code-block:: none

   (monitor) emulation SetGlobalSerialExecution true
