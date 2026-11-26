# AI regression photo set (Phase 0 / Phase 4 step 10 / Phase 8 step 3)

Put the ~10 fixed test photos here (`01.jpg` … `10.jpg`), each with an `NN.expected.json` listing the
items that are really in the photo, for example:

```json
{ "items": ["Top", "Bottoms", "Shoes", "Bag", "Eyewear"] }
```

Use photos you own or have the rights to (no children, no identifiable strangers without consent).

Run the set against the configured provider (costs money, so it is never part of `dotnet test` by default):

```powershell
$env:OF_AI_REGRESSION = "1"; dotnet test tests/OutfitFinder.Tests --filter Category=AiRegression
```

Results are written to `tests/photos/results-<provider>-<date>.md` for the provider comparison.
