# Legal Description Reference Templates

The Legal Description feature uses the reference resources in this directory as controlled project inputs.

## Runtime resource

`Basic Template.dotx` is configured as an `EmbeddedResource` and is embedded into the CLV Civil Tools assembly. The application therefore does not depend on an external copy of the template at runtime.

The Legal Description text-style and phrase-library JSON files in `CLV_CivilTools/Reference/` are likewise embedded into the assembly.

## Other reference examples

Older DOCX/PDF examples may be useful for development or comparison, but they are not runtime dependencies unless the implementation explicitly changes to use them.

If the City's official Legal Description template or reference language is revised, provide the new source document for review. Compare it with the current embedded template, text styles, phrase library, and generation logic before changing the application.