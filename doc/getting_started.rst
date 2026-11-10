..
   Copyright (c) 2026 Muhammad Waleed Badar
   SPDX-License-Identifier: GPL-3.0-only

Getting started
###############

Requirements
************

- `Renode <https://renode.io>`_ 1.16.0. The models are C# files that Renode
  compiles when a script includes them; nothing has to be built into Renode.
- A bare-metal Arm GCC for the boot ROM stand-in and the sample. The one in
  the Zephyr SDK is picked up from ``~/zephyr-sdk-*``; otherwise
  ``arm-none-eabi-gcc`` from the ``PATH`` is used. Set ``CROSS_COMPILE`` to
  choose another.
- GNU Make, and Python 3 for the tests.

Optional, for the other samples:

- A Zephyr workspace with ``west``, to build the Zephyr samples.
- The Raspberry Pi Pico SDK. A Zephyr workspace has a copy in
  ``modules/hal/rpi_pico``.

Building
********

.. code-block:: sh

   make

builds the boot ROM stand-in (``build/bootrom/bootrom.elf``) and the
bare-metal dual-core sample (``build/multicore/multicore.elf``). Renode loads
the ROM into every machine: without it neither core has anything to start
from.

Running the sample
******************

.. code-block:: sh

   make run

starts Renode with a Raspberry Pi Pico 2 and the dual-core sample, and
opens a window on UART0. Type ``start`` at the Monitor prompt.

Without a display, keep Renode in the terminal and send the UART to a file:

.. code-block:: sh

   renode --console --disable-xwt \
       -e 'include @scripts/rpi_pico2.resc; uart0 CreateFileBackend @uart0.log true; start'

Running another image
*********************

``scripts/rpi_pico2.resc`` loads the ELF file named by ``$elf``. Set it
before the script is included:

.. code-block:: sh

   renode -e '$elf=@/path/to/image.elf; include @scripts/rpi_pico2.resc; start'

The image has to be one the chip would boot: an Arm image for the RP2350
with an image definition (``IMAGE_DEF``) block in its first 4 kB. Both the
Pico SDK and Zephyr produce one. See :ref:`boot-rom` for what the ROM
stand-in accepts.

Zephyr
======

Build for the Pico 2 as for the board, and point ``$elf`` at the result:

.. code-block:: sh

   west build -b rpi_pico2/rp2350a/m33 zephyr/samples/hello_world
   renode -e '$elf=@build/zephyr/zephyr.elf; include @/path/to/rp2350_renode/scripts/rpi_pico2.resc; start'

.. code-block:: none

   *** Booting Zephyr OS build v4.5.0 ***
   Hello World! rpi_pico2/rp2350a/m33_0

Zephyr runs on core 0. Core 1 stays where the boot ROM parks it until
something launches it; :ref:`sample-zephyr-core1` does.

Pico SDK
========

.. code-block:: sh

   make pico-sdk PICO_SDK_PATH=/path/to/pico-sdk
   renode -e '$elf=@build/pico_sdk/sdk_multicore.elf; include @scripts/rpi_pico2.resc; start'

With ``ZEPHYR_BASE`` set, ``PICO_SDK_PATH`` defaults to the copy of the SDK
in that workspace.

In a script of your own
***********************

``scripts/rp2350.resc`` is the part to build on. It creates the machine,
compiles the models, loads ``platforms/rp2350.repl`` and the boot ROM, and
stops there:

.. code-block:: none

   $platform=@/path/to/my_board.repl
   include @/path/to/rp2350_renode/scripts/rp2350.resc

   macro reset
   """
       sysbus LoadELF $bootrom
       sysbus LoadELF @/path/to/image.elf
   """
   runMacro $reset

A board description starts from the chip and adds what is on the board, as
``platforms/rpi_pico2.repl`` does for the LED of the Pico 2:

.. code-block:: none

   using "./rp2350.repl"

   led: Miscellaneous.LED @ io_bank0 25

   io_bank0:
       25 -> led@0

Repository layout
*****************

.. list-table::
   :header-rows: 1
   :widths: 25 75

   * - Path
     - Contents
   * - ``platforms/``
     - ``rp2350.repl``, the chip, and ``rpi_pico2.repl``, the Pico 2 board
   * - ``peripherals/``
     - The C# models that Renode does not have
   * - ``bootrom/``
     - The boot ROM stand-in
   * - ``scripts/``
     - Renode scripts: ``rp2350.resc`` for the chip alone,
       ``rpi_pico2.resc`` for the board with an image
   * - ``samples/multicore/``
     - The dual-core checks on bare metal
   * - ``samples/pico_sdk/``
     - The dual-core checks through the Pico SDK
   * - ``samples/zephyr_core1/``
     - Zephyr on core 0 with a worker on core 1
   * - ``tests/``
     - Robot Framework tests for ``renode-test``
   * - ``doc/``
     - This documentation
