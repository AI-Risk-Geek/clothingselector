# Phase 0 — prove the core (manual, in the provider's playground)

Goal: know the AI can find the clothes and real product links **before** relying on it. Nothing in the app replaces this.

## Checklist

1. [ ] Pick the model. Write its exact name and provider here: `__________`
2. [ ] In the playground, turn on **image input** and **web search** together. If the model can't do both at once, stop: decision 1 needs a different model.
3. [ ] Paste the instructions from `src/OutfitFinder.Core/Ai/instructions.md` as the system message.
4. [ ] Set structured output to the JSON schema in `src/OutfitFinder.Core/Ai/outfit-analysis.schema.json` (strict).
   If the playground refuses search + strict JSON together, note "two calls needed" (the Gemini adapter already does this).
5. [ ] Run 10 real outfit photos (yours, or ones you have rights to — no children). Save them as `tests/photos/01.jpg` … `10.jpg`.
6. [ ] For each photo fill in one row below. Open **every** returned link.
7. [ ] Write `tests/photos/NN.expected.json` for each photo (the items really in it).
8. [ ] Compare with the bar at the bottom.

Once keys are configured you can repeat this automatically:

```powershell
$env:OF_AI_REGRESSION = "1"; dotnet test tests/OutfitFinder.Tests --filter Category=AiRegression
```

## Results template

| Photo | Items present | Items found | Box quality (good / ok / bad) | Links returned | Links that open a real product page | Prices correct? | Seconds | Cost (USD) | Notes |
|---|---|---|---|---|---|---|---|---|---|
| 01 | | | | | | | | | |
| 02 | | | | | | | | | |
| 03 | | | | | | | | | |
| 04 | | | | | | | | | |
| 05 | | | | | | | | | |
| 06 | | | | | | | | | |
| 07 | | | | | | | | | |
| 08 | | | | | | | | | |
| 09 | | | | | | | | | |
| 10 | | | | | | | | | |
| **Total** | | | | | | | **avg** | **avg** | |

Search + strict JSON in one call? ☐ yes ☐ no (two calls)

## Done when

* ☐ Items found ÷ items present ≥ **80%**
* ☐ Live product pages ÷ links returned ≥ **60%**
* ☐ Every search under **2 minutes**
* ☐ Cost per photo known: `$______`

If any box is unticked, stop and revisit the search design before launch.
