..
   Copyright (c) 2026 Muhammad Waleed Badar
   SPDX-License-Identifier: Apache-2.0

Getting started
###############

Requirements
************

- `Renode <https://renode.io>`_ 1.16.0. The models are C# files that Renode
  compiles when a script includes them; nothing has to be built into Renode.
- ``west`` and what a Zephyr build needs: CMake, Python 3 and the
  `Zephyr SDK <https://docs.zephyrproject.org/latest/develop/toolchains/zephyr_sdk.html>`_
  with the ``arm-zephyr-eabi`` toolchain.

.. _workspace:

The west workspace
******************

This repository is a Zephyr module and the manifest repository of a west
workspace, laid out like the Zephyr
`example application <https://github.com/zephyrproject-rtos/example-application>`_.
``west.yml`` names Zephyr and the modules that a build for the RP2350
needs, and ``west`` fetches them. Zephyr is pinned to the head of
`pull request 124714 <https://github.com/zephyrproject-rtos/zephyr/pull/124714>`_,
which adds SMP for Cortex-M and is not merged yet; see :ref:`zephyr-smp`.

.. code-block:: sh

   west init -m https://github.com/walidbadar/Renode_RP2350 --mr main my-workspace
   cd my-workspace
   west update

With the repository already cloned, ``west init -l Renode_RP2350`` in the
directory above it does the same. The workspace then holds:

.. code-block:: none

   my-workspace/
   ├── Renode_RP2350/          this repository
   ├── zephyr/                 Zephyr, at the pull request
   └── modules/hal/
       ├── cmsis_6/
       └── rpi_pico/

The commands on these pages are run from the top of the workspace.

Linking to an existing workspace
================================

To use a Zephyr workspace you already have instead, link the repository
into it and make it the workspace's manifest:

.. code-block:: sh

   cd ~/zephyrproject
   ln -s /path/to/Renode_RP2350 Renode_RP2350
   west config manifest.path Renode_RP2350
   west update zephyr

``west update zephyr`` checks out the Zephyr commit that ``west.yml`` pins,
so commit or stash any work in ``zephyr/`` first. Run ``west update``
without a name instead if ``cmsis_6`` or ``hal_rpi_pico`` is missing or at
another revision. While the link is the manifest, only Zephyr and those two
modules are part of the workspace; to go back:

.. code-block:: sh

   west config manifest.path zephyr
   west update

The board
*********

``boards/renode/cortex_m33`` is a Zephyr board for the model: the
RP2350A laid out like a Pico 2, with the console on UART0 and an LED on
GPIO25. It has two variants:

.. list-table::
   :header-rows: 1
   :widths: 40 60

   * - Build for
     - What runs
   * - ``cortex_m33/rp2350a/m33_0``
     - Zephyr on core 0. Core 1 stays where the boot ROM parks it until
       something launches it, as :ref:`sample-zephyr-core1` does.
   * - ``cortex_m33/rp2350a/m33/smp``
     - One Zephyr kernel on both cores (``CONFIG_SMP``); see
       :ref:`zephyr-smp`.

The targets follow Zephyr's naming for the chip: ``rp2350a`` is the SoC,
``m33_0`` the Cortex-M33 in the first socket, and ``m33`` a Cortex-M33 in
both, which is what the ``smp`` variant runs on.

The Pico 2 board that Zephyr has gets an SMP variant as well,
``rpi_pico2/rp2350a/m33/smp``, from ``boards/raspberrypi/rpi_pico2``. Its
images, and those of the other variants of that board, run on the model
too.

Every Arm build for the RP2350 in this workspace, for any board, also
builds the boot ROM stand-in and leaves it
next to the image, as ``build/zephyr/rp2350_bootrom.elf``. Renode loads the
ROM into every machine: without it neither core has anything to start from.

Running a sample
****************

.. code-block:: sh

   west build -b cortex_m33/rp2350a/m33_0 zephyr/samples/hello_world
   west build -t run

starts Renode with the image and prints UART0 in the terminal. Any Zephyr
application is built and run the same way, and on both cores with the
other variant:

.. code-block:: sh

   west build -p -b cortex_m33/rp2350a/m33/smp zephyr/samples/philosophers -t run

Running another image
*********************

``scripts/rpi_pico2.resc`` loads the ELF file named by ``$elf`` on a
Raspberry Pi Pico 2. Set it before the script is included:

.. code-block:: sh

   renode -e '$elf=@build/zephyr/zephyr.elf; include @Renode_RP2350/scripts/rpi_pico2.resc; start'

This opens a window on UART0. Without a display, keep Renode in the
terminal and send the UART to a file:

.. code-block:: sh

   renode --console --disable-xwt \
       -e '$elf=@build/zephyr/zephyr.elf; include @Renode_RP2350/scripts/rpi_pico2.resc; uart0 CreateFileBackend @uart0.log true; start'

The boot ROM stand-in is looked for next to the image, as
``rp2350_bootrom.elf``. For an image that was not built for the
``cortex_m33`` board, one built for ``rpi_pico2/rp2350a/m33_0`` for instance,
say where the ROM of some other build is:

.. code-block:: sh

   renode -e '$bootrom=@build/zephyr/rp2350_bootrom.elf; $elf=@/path/to/image.elf; include @Renode_RP2350/scripts/rpi_pico2.resc; start'

The image has to be one the chip would boot: an Arm image for the RP2350
with an image definition (``IMAGE_DEF``) block in its first 4 kB. Zephyr
produces one. See :ref:`boot-rom` for what the ROM stand-in accepts.

In a script of your own
***********************

``scripts/rp2350.resc`` is the part to build on. It creates the machine,
compiles the models, loads ``platforms/rp2350.repl`` and the boot ROM, and
stops there. ``$bootrom`` has to be set first, or ``$elf``, next to which
the ROM is then looked for:

.. code-block:: none

   $platform=@/path/to/my_board.repl
   $bootrom=@/path/to/rp2350_bootrom.elf
   include @/path/to/Renode_RP2350/scripts/rp2350.resc

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
   * - ``west.yml``
     - The manifest of the workspace
   * - ``zephyr/module.yml``
     - What makes the repository a Zephyr module
   * - ``boards/renode/cortex_m33/``
     - The Zephyr board for the model
   * - ``boards/raspberrypi/rpi_pico2/``
     - An SMP variant for Zephyr's Pico 2 board
   * - ``soc/rp2350/``
     - What Zephyr needs from the RP2350 to run on both cores
   * - ``drivers/timer/``
     - The system timer of the board, a TIMER block instead of SysTick
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
   * - ``samples/zephyr_core1/``
     - Zephyr on core 0 with a worker on core 1
   * - ``tests/``
     - The tests that twister runs, with their Robot Framework files
   * - ``doc/``
     - This documentation
