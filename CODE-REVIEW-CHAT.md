# SnakeGA — Code Review & GA Design Discussion

**Project:** `SnakeG-main` (SnakeGA.Server + snakega.client)
**Date:** 2026-09-25
**Participants:** User, Claude Code (Opus 5)

---

## User

> can you review this project? Do you see any major issues here? Can you also check the GA, I think I need a shortest path alg for the food and if the snake makes less than this amount of moves, we kill it and maybe I will need to add a point system for longest time alive to prevent the snake from unaliving itself aftering eating an apple, what you think?

---

## Claude

I've read through the whole thing. Solid architecture overall — the BFS-based health budget, replay-by-food-history for the champion, and the ref-based canvas rendering are all genuinely good ideas. But there are some real bugs, including one that's almost certainly *causing* the suicide behavior you're asking about.

### Major issues

#### 1. You're broadcasting 101 neural networks to every client, 10× per second

`GameState.Brain` is not `JsonIgnore`'d (`Dtos/GameState.cs:8`), and `NeuralNetwork` exposes `Weights` (26×64 + 64×4 = 1920 floats) and `Biases` (68 floats) as public properties. Every `ReceiveTick` serializes all of that for all 100 snakes plus `best`:

```
~1988 floats × ~12 bytes of JSON × 101 snakes ≈ 2.4 MB per tick × 10 ticks/s ≈ 24 MB/s
```

Your client `types.ts` doesn't even have a `brain` field — it's 100% waste. This is very likely the single biggest thing making the app feel heavy.

```csharp
public record class GameState
(
    List<Point> Body,
    Point Food,
    int Health,
    [property: JsonIgnore] NeuralNetwork Brain,   // <-- add this
    ...
);
```

#### 2. Your scoring pays the snake 9,900 points to kill itself — this is your suicide bug

`PointForLooping = -100` but `DeathPenalty = -10_000` (`SimulationControl.cs:18-19`). Every snake dies eventually, so every snake pays a death cost — *except* one that dies by looping.

Run the numbers on a snake that just ate its first apple (+15,000):

| Then it... | Final fitness |
|---|---|
| loops into a cell 3× and dies | ~14,900 |
| hits a wall / itself / starves | ~5,000 |

Suicide-by-looping is worth **+9,900** over any other death. And `Visited` is cleared on every apple (`SnakeService.cs:448`), so right after eating, the cheapest available action is to trace a small square three times and die. That is exactly the "unaliving itself after eating an apple" behavior you're describing — it's not a missing survival bonus, it's the fitness function actively rewarding it.

It's also poisoning gen 1: before any snake has eaten anything, "find a tight loop and die" (-100) beats "explore and crash into a wall" (-10,000), so early evolution optimizes for looping.

Fix: make looping death cost *at least* as much as normal death. The loop penalty should be an *additional* penalty, not a replacement:

```csharp
if (visitCount >= control.NumberOfRepeats)   // >= not ==, see below
{
    pointsModifier += control.DeathPenalty + control.PointForLooping;
    return state with { Points = state.Points + pointsModifier, IsDead = true };
}
```

Also, `visitCount == control.NumberOfRepeats` (`SnakeService.cs:410`) uses `==`. Since the settings endpoint has no validation, setting `NumberOfRepeats` to 0 silently disables loop-death forever. Use `>=`.

And separately: `NumberOfRepeats = 3` is harsh. On a 60×30 board, legitimately navigating around your own body often requires crossing a cell 3 times. I'd start at 5-8.

#### 3. Any exception in the sim loop takes down the whole server

`ExecuteAsync` has no try/catch, and .NET's default `BackgroundServiceExceptionBehavior` is `StopHost`. There are several reachable throws, all made reachable by `/api/settings` having **zero validation** (`Program.cs:44`):

- `TournamentSize = 0` → `bestContender` stays null → `NullReferenceException` (`SnakeService.cs:233`)
- `NumberOfParents = 0` → `parents[Random.Shared.Next(0)]` → `ArgumentOutOfRangeException` (`SnakeService.cs:194`)
- `ElitismCount = 0` with an empty `validStates` → same class of problem

Clamp the inputs in the endpoint, and wrap the loop body in a try/catch that logs and keeps going.

#### 4. `PickLocation` crashes if the board fills up

```csharp
int index = Random.Shared.Next(empty.Count);   // Next(0) == 0
return empty[index];                            // IndexOutOfRangeException
```

That's your win condition crashing the server (and per #3, taking the host with it). Needs an `if (empty.Count == 0)` guard that ends the game as a win.

#### 5. Dead snakes render as living ones

In `CanvasGrid100.tsx`, lines 44-64 branch on `isDead` and carefully draw the dead body in gray with a red tint — and then lines 75-95 unconditionally redraw food and body in living colors on top of it. The whole dead/alive branch is dead code. Delete lines 75-95 (the duplicated "Draw Food" / "Draw Snake Body" / HUD block); the branch above already handles both cases.

Also `gameState.isDead` on line 45 dereferences a `GameState | null` without a check. It compiles only because `strict` is missing from `tsconfig.app.json` — worth turning on.

#### 6. You can never stop a fast-forward

`control.TargetGeneration += count` (`Program.cs:40`) only ever increases, and the loop's only exit is `gen` catching up. Fat-finger `100000` and your only option is restarting the server. Add a stop endpoint that sets `TargetGeneration = gen`.

### On your GA questions specifically

**Shortest path: you already built it.** `GetTrueShortestPath` (`SnakeService.cs:267`) is a correct BFS with the body as obstacles, and `UpdateFood` already uses it as the health budget: `Health = exactDistance + HealthOffset`. So "kill it if it takes more moves than the shortest path" is *already the mechanic you have*. You don't need to add it — you need to fix two things in it:

- **The unreachable-food fallback is wrong.** When the body walls the food off, BFS exhausts and you `return GetDistance(start, food)` — Manhattan distance, which is *shorter* than the (impossible) real path. The snake gets a tiny budget for a situation that's already unwinnable, so it starves almost immediately and eats a -10,000. Either return a generous budget (`width * heigth`) or, better, treat the tail cells as passable — they *will* have moved by the time the head arrives, so BFS-with-static-body systematically overestimates difficulty.
- **`HealthOffset = 50` is quite tight** for a 60×30 board once the snake is long. It punishes necessary detours around its own body. I'd scale it with length rather than keep it constant.

**Survival points: your instinct is right, but be careful which direction you push.** Adding a flat per-step reward is the classic way to get the *opposite* failure — a snake that circles forever and never eats. The better framing, and it's cheap because you already have BFS:

- Fix the looping penalty (#2). That alone should stop most of the post-apple suicides, because suicide will no longer be profitable.
- Use steps as a **tiebreaker**, not a driver. The standard formulation is `fitness = apples² × C + steps`, where the apple term always dominates. Steps only separate snakes with equal apple counts.
- Since you have the exact shortest path, reward **efficiency** directly: scale the apple reward by `shortestPath / movesActuallyTaken`. A snake that walks the optimal route gets full credit, one that wanders gets less. That's a much sharper signal than "time alive" and it's ~5 lines given what you already have.

**Two other GA notes:**

- **`applesEaten` is off by one** (`SnakeService.cs:441`): `body.Count - initialSize` is already `1` on the first apple, because `body.Add(newHead)` ran before `MoveSnake`. So the first apple pays 15,000 (`10000 + 1×5000`), and base `EatenApplePoints` is never awarded alone. Should be `body.Count - initialSize - 1`.
- **Turn 1 of every game runs against phantom food at (-1,-1).** `UpdateBody` is called before `UpdateFood` (`SnakeService.cs:78-88`), so on the first tick the network sees food deltas of roughly `(-5/60, 0/30)` and gets scored on "moving toward" a cell off the board. Call `UpdateFood` before `UpdateBody`, or seed food at spawn.

### Smaller stuff

- `SimulationControl` is a singleton mutated from HTTP threads and read from the sim thread with no synchronization. 32-bit writes won't tear, so in practice you'll be fine, but `InjectedBrain` has a genuine read-then-null race in `GetNextGenBrains` — worst case you drop an imported brain.
- `SnakeService.cs:106` passes `_bestFoodHistory` by reference into the replay state, which then appends to it once the replay outlives the recorded history. Use `[.. _bestFoodHistory]`.
- `_gameStates.Add(key, null)` at `SnakeService.cs:75` is a no-op — overwritten at line 89.
- Hot-path allocations: `GetVision` builds a fresh `HashSet<Point>` of the body every tick for every snake, and `body.Contains(newHead)` is an O(n) list scan. Keeping one `HashSet` per snake alongside the body list would meaningfully speed up fast-forward.
- Weights are permanently clamped to [-1, 1] (creep mutation clamps, random reset draws in range). Not crippling with tanh, but it's an invisible ceiling on expressiveness.
- Uniform `[-1,1]` init rather than Xavier/He — with 26 inputs your hidden layer starts fairly saturated.
- `vite.config.ts` still only proxies the scaffolded `/weatherforecast`, which is why `App.tsx` hardcodes `https://localhost:7135` in five places and `Program.cs` hardcodes origin `55698`. Proxy `/api` and `/snake` (with `ws: true`) and use relative URLs.
- Typos: `heigth` (throughout), `"Sfaety check"` (`SnakeService.cs:230`), `extaPoints` (`SnakeService.cs:457`).
- `bin/`, `obj/`, and `.vs/` are sitting in the folder. If you put this under version control, add a `.gitignore` first.

### If you only do three things

1. `[property: JsonIgnore]` on `GameState.Brain` — instant ~24 MB/s → ~50 KB/s.
2. Make looping death cost `DeathPenalty + PointForLooping`. This is your suicide bug.
3. Wrap the sim loop in try/catch and clamp `/api/settings` inputs.

---

## User

> I fixed the json and the dealth for looping. I will check the rest later, but there is some nasty bug here. There are gens where the snake gets 20k points and the next one ends at 5k points, I get it that if apples spawn in a particular way, the snake just follows a path and can get more points but it's kinda wild to me that the next gen is worse than the last and I feel like there's more to it than just lucky spawns of apples

---

## Claude

Good — that's the two biggest ones out of the way. Let me address the variance, because I think there's a concrete answer here and it's only partly "lucky spawns."

### First: 20k → 5k is literally one apple

Work out what your score function actually pays. `applesEaten = body.Count - initialSize` evaluated *after* `body.Add(newHead)`, so:

| Apples | Cumulative apple reward | − DeathPenalty | Final score |
|---|---|---|---|
| 1 | 15,000 | −10,000 | **~5,000** |
| 2 | 35,000 | −10,000 | **~25,000** |
| 3 | 60,000 | −10,000 | ~50,000 |
| 4 | 90,000 | −10,000 | ~80,000 |

(plus a few hundred from `RightDirectionPoints`)

So "20k then 5k" is **two apples then one apple**. That's it. Your reward is super-linear (`ExtraApplesMultiplier` makes apple *N* worth `10000 + 5000N`) sitting on top of a flat −10,000 that everyone pays. The result is that a one-apple difference renders as a 4-5× collapse. A big chunk of the "wildness" is a display artifact of the scoring scale.

### Second: you're systematically selecting for luck, not just occasionally getting it

This is the part that's more than bad luck, and I think it's what you're sensing.

Every snake's score is `brain_quality + food_luck`. You report and select on `max()` over 100 snakes. The argmax of 100 noisy samples is biased hard toward the **largest noise term** — you don't occasionally promote a lucky snake, you promote the luckiest one *every single generation, by construction*.

Then in gen N+1 that brain is re-evaluated against fresh random food, and its luck term resets to average. So the champion's score doesn't *sometimes* drop — **on average it must drop, every time**. The sawtooth is the expected behavior of this setup, not a symptom of something intermittent.

Elitism doesn't protect you, because `GetNextGenBrains` preserves the *brain object*, not the *score*. `CreateOffsprings` hands it a fresh `GameState` with new random food. You keep the genotype and throw away the evaluation.

And the noise term here is enormous relative to real progress: one lucky apple is worth +20,000, while genuine gen-to-gen brain improvement early in training is worth approximately zero. Signal is completely buried.

### A clean experiment to confirm it (5 minutes, no code changes)

You already have everything needed:

- **Snake #0 in gen N+1 is bit-for-bit the gen N champion.** `validStates[0].Brain` → `nextGenerationBrains[0]` → `_gameStates["0"]`. Same object, same start position, only the food differs.
- **The `best` replay snake is that same brain on the *recorded* food.** It's fully deterministic — same brain, same start, `FoodHistory` replayed from `_bestFoodHistory`.

So switch to the Best AI view and compare three numbers: last gen's reported champion, the `best` replay's final score, and snake #0's score in the new gen.

- Replay == last gen's champion, but snake #0 is way lower → **pure food luck**, confirmed. No bug.
- Replay != last gen's champion → now we have a real determinism bug and I'll go dig for it.

### Fixes, in order of impact

**1. Give every snake in a generation the same food sequence.** This is the big one and it's cheap. Right now snake #0 and snake #57 are being compared on completely different problems, which makes within-generation ranking (and therefore *all* your selection) mostly noise. Pre-generate a candidate stream per generation and have `PickLocation` walk it:

```csharp
private List<Point> _foodPlan = [];

private void BuildFoodPlan(int seed)
{
    Random rng = new(seed);
    _foodPlan = [.. Enumerable.Range(0, 512).Select(_ => new Point(rng.Next(width), rng.Next(heigth)))];
}

private Point PickLocation(List<Point> body, ref int planIndex)
{
    HashSet<Point> taken = [.. body];
    while (planIndex < _foodPlan.Count)
    {
        Point p = _foodPlan[planIndex++];
        if (!taken.Contains(p)) return p;
    }
    // fall back to the existing scan if the plan runs out
}
```

Store `planIndex` on `GameState`. Two snakes that play identically get identical food; they diverge only as their bodies diverge, which is exactly what you want. Bonus: this also kills the `PickLocation` full-board crash and is much faster than building an 1800-element list every apple.

**2. Evaluate each brain on K episodes and average.** K=3 with three different seeds, fitness = mean (or min, if you want robustness). Costs 3× sim time, but fast-forward is nearly free and this is what actually separates "good brain" from "lucky brain." Without it, no amount of tuning will produce a monotone curve.

**3. Stop tracking the population max.** Max is the single worst progress metric under noise — it's the order statistic most contaminated by it. Log and plot instead:
- **median** fitness across the population
- **mean apples eaten** (not points — apples, so you're reading a linear quantity)
- the elite's re-evaluated score

Median and mean-apples will show a clean upward trend while the max is still sawtoothing. I'd bet your training is actually working fine and the metric is lying to you.

### Three secondary noise sources still in the code

- **BFS unreachable-food fallback.** When the body walls off the food, `GetTrueShortestPath` falls through to `return GetDistance(start, food)` — Manhattan, i.e. *shorter* than the real path. The snake gets a tiny health budget for an already-hard position and starves. This fires semi-randomly depending on body shape and injects real variance into fitness.
- **Turn-1 phantom food.** `UpdateBody` runs before `UpdateFood`, so the first move of every game is made against food at `(-1, -1)` and scored for "moving toward" an off-board cell. Randomizes the opening.
- **Per-weight uniform crossover.** 97 of your 100 snakes are produced by element-wise mixing of two different networks. This is the competing-conventions problem — two networks can both work while using totally different internal representations, so mixing them per-weight usually produces something worse than either parent. Combined with ~99 mutated genes per baby (1988 params × 0.05), most of your population is near-noise each generation, which widens the distribution the max is drawn from and makes the sawtooth worse. Swapping **whole hidden neurons** (all 26 incoming weights + bias taken from one parent together) preserves functional units and is a one-loop change.

Also, `applesEaten` is still off by one — it's `1` on the very first apple, so base `EatenApplePoints` is never awarded alone.

---

## User

> I think I need to add a tracker for longest alive period and rate them based on that and then points. I will also enforce an instant kill if it takes you longer to eat an apple than the BFS algorithm has calculated (no leeway), that way I can eliminate lucky spawns from the equation and I can bump up the looping penalty so that no looping counts as stayed alive for longer than when some eat apples. Do you think this is a good plan?

---

## Claude

Honest answer: the **goal** is right — you want apples to dominate and time-wasting to be punished. But two of the three mechanisms will backfire, and one of them can't work as described. Let me take them in order.

### #3 can't bite, and #2 makes it redundant

You're proposing to rank on survival time *first*, points *second*. That's a lexicographic ordering — and in a lexicographic ordering the secondary key **can never override the primary one**. A looping snake that survives 50 steps outranks an apple-eater that survives 49 steps regardless of whether the looping penalty is −100 or −10,000,000. The penalty lives in `Points`, which only gets consulted on exact ties in step count.

Separately: a BFS shortest path is a *simple* path — it never revisits a cell. So under zero leeway, revisiting any cell even **twice** already means you've blown the budget and are dead. `NumberOfRepeats` and `PointForLooping` become dead code. #2 fully subsumes #3.

### #1 is inverted, and it amplifies the luck you're trying to remove

Ranking survival-first means "stayed alive" beats "ate an apple." Concrete case under your zero-leeway rule:

- **Snake A**: first food spawns 30 BFS away. Never eats. Dies at step 30.
- **Snake B**: first food spawns 8 away. Eats it on a perfect path at step 8. Second food 6 away, misses, dies at step 14.

Survival-first ranks **A (0 apples, 30 steps) above B (1 apple, 14 steps)**. And look at what decided it: *spawn distance*. So survival-as-primary doesn't neutralize lucky spawns — it promotes spawn distance to being the dominant fitness term. It makes the exact problem you're attacking worse.

You want apples primary, survival as tiebreaker. You've got the priority backwards.

### #2 (zero leeway) has three problems

**It removes the gradient you need to bootstrap.** A random network walking a 20-cell BFS-optimal path requires 20 consecutive perfect moves. Nothing in generation 1 will ever eat a single apple, so the entire apple reward — your actual objective — becomes unreachable. Your only remaining signal is `RightDirectionPoints` at 1/step. GAs climb gradients; zero leeway replaces the gradient with a cliff. This is the standard sparse-reward failure and it's the biggest risk in the plan.

**It forces greedy play, so you can never evolve safe play.** Zero leeway means the only survivable move at every step is the one on the shortest path. But the shortest path is exactly what kills long snakes — it's the route that walks you into your own body and traps you. You'd be hard-coding "always be maximally greedy" as a survival requirement, which makes it impossible to ever evolve the detour-and-stay-safe behavior that a good snake needs at length 40+.

**It's sometimes literally impossible.** Two cases:
- `GetTrueShortestPath` treats the body as a *static* wall, computed once at spawn. A path of length *d* that doubles back near the snake's own body can be unwalkable in practice, because the tail hasn't cleared by the time the head arrives. Zero leeway turns "BFS was slightly optimistic" into guaranteed death.
- Your unreachable-food fallback (`return GetDistance(start, food)`, still unfixed) returns Manhattan distance when the food is walled off. With zero leeway that's an instant, unavoidable death on a spawn the snake had no way to solve. You'd be injecting random deaths — more variance, not less.

**And it doesn't remove spawn luck anyway.** Snake #0 and snake #57 still face *different* food sequences. All zero-leeway does is make every spawn hard; the *variance between spawns* is untouched, and arguably widens, since success probability falls off non-linearly with path complexity. Spawn luck is a variance problem, and you fix variance by controlling the variable — shared food sequence per generation, multi-episode averaging — not by raising the difficulty. Those two things are orthogonal to everything else here.

### What I'd do instead

**Collapse to one scalar fitness where apples provably dominate.** This gets you everything you wanted from #1 and #3 in one line, with no inversion and no need for a giant looping penalty:

```csharp
int fitness = applesEaten * 1000 + Math.Min(stepsAlive, 999);
```

Because the tiebreaker is capped below the apple weight, **one more apple always beats any amount of extra survival** — arithmetically guaranteed, no lexicographic edge cases. Survival now does exactly the job you want: it separates snakes that ate the same number of apples, and nothing more. Looping stops being profitable automatically, because looping doesn't increment `applesEaten`. If you want to reward going deep, use `applesEaten * applesEaten * 1000 + Math.Min(stepsAlive, 999)`.

Note this also means you can drop `DeathPenalty`, `PointForLooping`, `RightDirectionPoints`, `EatenApplePoints`, and `ExtraApplesMultiplier` entirely — every snake dies, so a flat death penalty is a constant offset that changes no ranking. Five hyperparameters you no longer have to tune.

**Make efficiency a graded reward, not a kill condition.** You already compute the exact BFS distance, so:

```csharp
float efficiency = (float)bfsDistanceAtSpawn / stepsTakenToReachApple;  // 1.0 == perfect
```

Fold that into the apple term. A snake that walks the optimal path gets full credit; one that wanders gets partial. That's a smooth slope toward optimality instead of a cliff — small improvements yield small reward increases, which is precisely what a GA can climb. Instant-kill gives it nothing to climb.

**Keep a kill condition, but generous, and anneal it.** Use slack rather than a fixed offset — `health = bfsDistance * slack` — and walk `slack` down as mean apples-per-snake rises: 3.0 early, 1.5 mid, 1.2 late. That's curriculum learning, and it gets you to near-optimal play without the bootstrap problem. You get the anti-time-wasting effect of #2 without the cliff.

**Then fix the spawn variance separately**, with the shared per-generation food plan and multi-episode averaging. That's the actual answer to your original complaint, and none of #1–#3 substitute for it.

So: keep the instinct (apples dominant, survival as tiebreaker, efficiency matters), invert the priority, and turn the hard kill into a graded reward plus an annealed slack budget.

---

## Open items / TODO

Fixed during the conversation:

- [x] `[property: JsonIgnore]` on `GameState.Brain`
- [x] Looping death penalty no longer cheaper than normal death

Still outstanding:

- [ ] Wrap `ExecuteAsync` loop body in try/catch (unhandled exception currently stops the host)
- [ ] Validate/clamp `/api/settings` inputs (`TournamentSize`, `NumberOfParents`, `ElitismCount`, `NumberOfRepeats`)
- [ ] `PickLocation` crash when the board fills up
- [ ] Remove duplicated draw block in `CanvasGrid100.tsx:75-95` (dead snakes render as alive)
- [ ] Enable `strict` in `tsconfig.app.json`; fix the `GameState | null` deref
- [ ] Add an endpoint to stop/reset a fast-forward
- [ ] Fix BFS unreachable-food fallback (returns Manhattan, which is too short)
- [ ] `applesEaten` off-by-one (`body.Count - initialSize - 1`)
- [ ] Call `UpdateFood` before `UpdateBody` (turn-1 phantom food at `(-1,-1)`)
- [ ] `visitCount >= NumberOfRepeats` instead of `==`
- [ ] Copy `_bestFoodHistory` by value at `SnakeService.cs:106`
- [ ] Remove no-op `_gameStates.Add(key, null)` at `SnakeService.cs:75`
- [ ] Shared per-generation food plan (fair within-generation comparison)
- [ ] Multi-episode evaluation with averaged fitness
- [ ] Track median fitness + mean apples instead of population max
- [ ] Single scalar fitness: `apples * 1000 + min(steps, 999)`
- [ ] Graded efficiency reward + annealed slack instead of a hard BFS kill
- [ ] Neuron-level crossover instead of per-weight uniform crossover
- [ ] Vite proxy for `/api` and `/snake` (`ws: true`); drop hardcoded ports
- [ ] Per-snake `HashSet<Point>` for body lookups (hot path)
- [ ] `.gitignore` before putting this under version control
