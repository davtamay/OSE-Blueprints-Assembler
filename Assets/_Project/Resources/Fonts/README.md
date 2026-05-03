# UIToolkit Runtime Font

Drop **`Inter-Regular.ttf`** (or any full-coverage TTF) here. `UIDocumentBootstrap`
loads it from `Resources/Fonts/Inter-Regular` and assigns it to the runtime
PanelSettings via a generated `PanelTextSettings`. Without this file the
WebGL build falls back to Unity's stripped default font, which silently
misses symbol glyphs (▶ ⚒ ⊞ etc.).

Source: https://rsms.me/inter/ — OFL license, ~310 KB.

If you want a different font, change `FontResourcePath` in
`Assets/_Project/Scripts/UI/Bindings/UIDocumentBootstrap.cs` to match.
