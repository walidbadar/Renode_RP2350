..
   Copyright (c) 2026 Muhammad Waleed Badar
   SPDX-License-Identifier: Apache-2.0

Licence
#######

rp2350_renode is licensed under the Apache-2.0, as Zephyr is. That covers
the peripheral models, the platform descriptions and scripts, the boot ROM
stand-in, the Zephyr board, the samples and the tests.

The boot ROM stand-in is not the Raspberry Pi boot ROM and shares no code
with it. It implements the launch protocol and the lookup interface as the
RP2350 datasheet and the Pico SDK describe them.

Renode, its Cortex-M33, NVIC and PL011 models and its test runner come from
Antmicro under their own licences. Zephyr and its modules, which the
optional samples are built with, are not part of this repository.

Raspberry Pi is a trademark of Raspberry Pi Ltd. Renode is a trademark of
Antmicro. Arm and Cortex are trademarks of Arm Limited. Zephyr is a
trademark of The Linux Foundation. This project is not affiliated with or
endorsed by any of them.
