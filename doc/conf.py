# Copyright (c) 2026 Muhammad Waleed Badar
# SPDX-License-Identifier: Apache-2.0

# Sphinx configuration of the Renode_RP2350 documentation.
#
#   pip install -r doc/requirements.txt
#   sphinx-build -b html doc doc/_build/html

project = "Renode_RP2350"
author = "Muhammad Waleed Badar"
copyright = "2026, Muhammad Waleed Badar"

extensions = [
    "sphinx.ext.githubpages",
]

exclude_patterns = ["_build", "requirements.txt"]

primary_domain = "c"
highlight_language = "none"

html_theme = "furo"
html_title = "Renode_RP2350"
html_logo = "images/renode-rp2350-logo.svg"
html_favicon = "images/renode-rp2350-logo.svg"
html_static_path = []
html_theme_options = {
    "source_repository": "https://github.com/walidbadar/Renode_RP2350",
    "source_branch": "main",
    "source_directory": "doc/",
}
