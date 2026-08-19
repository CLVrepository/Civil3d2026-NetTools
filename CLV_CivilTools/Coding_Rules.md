\------------------------------

**Source Files:**

* CLV\_CivilTools\_DATE.zip - "DATE" will refer to the date uploaded
* Pipe Materials.zip - pipe sizes and material specs used for the UFLS parts.





\------------------------------

**Project Architecture Data:**

* Review all \*\*.cs files
* Review PROJECT\_MAP.md, COMMAND\_INDEX.md, and CHANGELOG.md files





\------------------------------

**Constraints:**

* Keep WinForms PaletteSet approach
* net8.0-windows, x64, nullable enable
* Avoid ambiguous Color/Exception issues
* AutoCAD keyword prompts must use a unique keyboard shortcut letter for every option shown in the same prompt. Never allow two options to share the same accelerator (for example, `Same/Separate`). The accelerator must be encoded in the ACTUAL keyword token registered with `PromptKeywordOptions.Keywords.Add(...)`, not only in a separate display name. Example: register `Same` and `seParate` so AutoCAD accepts `S`/`Same` and `P`/`Separate`. After implementation, verify both the one-letter accelerator and the complete typed keyword work for every option, and verify `StringResult` maps to the intended internal choice. Review every new or modified `PromptKeywordOptions` / `GetKeywords` prompt before packaging.
* Provide downloadable zip file including all NEW files, MODIFIED files, and UNCHANGED files to keep the project structure working
* Update the PROJECT\_MAP.md file
* Update the COMMAND\_INDEX.md and CHANGELOG.md file
* All lisp routines that are referenced, should point to our server pathing, NOT as a local helper approach; \\\\ci.las-vegas.nv.us\\pw\_data\_depot\\PW\_AutoCAD\_Support\\2026\_Civil3D\\Lisp\\Lisp\\...
* Notify me if new lisp routines are created so I can copy them to the correct location.

