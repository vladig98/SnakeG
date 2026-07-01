using Microsoft.AspNetCore.SignalR;
using SnakeGA.Server.Dtos;
using SnakeGA.Server.Hubs;

namespace SnakeGA.Server.Services;

public class SnakeService(IHubContext<SnakeHub> hubContext, SimulationControl control) : BackgroundService
{
    private NeuralNetwork? _bestPreviousBrain = null;
    private readonly Dictionary<string, GameState?> _gameStates = [];
    private readonly List<GameState> _finalStates = [];
    private readonly Dictionary<string, bool> _gameOver = [];
    private const int populationSize = 100;
    private const int width = 60;
    private const int heigth = 30;
    private const int initialSize = 5;
    private const int tournamentSize = 5;
    private const int numberOfParents = 10;
    private const string best = "best";
    private int gen = 1;
    private const int eatenApplePoints = 10_000;
    private const int wrongDirectionPoints = 0;
    private const int rightDirectionPoints = 1;
    private const int pointForLooping = -100;
    private const int deathPenalty = -10_000;
    private const int elitismCount = 3;
    private bool _readyToEvolve = false;
    private List<Point> _bestFoodHistory = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield(); // Let the web server boot

        int fastForwardFrameCounter = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            // 1. Are we paused on a graveyard?
            bool isPaused = gen >= control.TargetGeneration && _readyToEvolve;

            if (isPaused)
            {
                await Task.Delay(100, stoppingToken);
                continue; // Skip the rest of the loop
            }

            // 2. Are we trying to catch up to the Target Generation?
            bool isFastForwarding = gen < control.TargetGeneration;

            // Calculate the math for the frame
            var gameStates = GenerateNextGenerationData();

            if (isFastForwarding)
            {
                // FAST FORWARD MODE
                // We do NOT broadcast to React, and we do NOT delay time.
                fastForwardFrameCounter++;

                // Every 1,000 frames, yield control back to the CPU for a microsecond 
                // so your React frontend can still talk to the backend API!
                if (fastForwardFrameCounter % 1000 == 0)
                {
                    await Task.Yield();
                }
            }
            else
            {
                // NORMAL PLAYBACK MODE
                // We reached the target generation! Broadcast it so you can watch it play out.
                var payload = new { Generation = gen, Players = gameStates };
                await hubContext.Clients.All.SendAsync("ReceiveTick", payload, stoppingToken);

                // Keep the 10 FPS delay so human eyes can track the movement
                await Task.Delay(100, stoppingToken);
            }
        }
    }

    private Dictionary<string, GameState?> GenerateNextGenerationData()
    {
        // 1. If the graveyard is on screen and we are unpaused, do the evolution!
        if (_readyToEvolve)
        {
            EvolveNewGeneration();
            _readyToEvolve = false;
            return _gameStates;
        }

        bool allDead = true;
        for (int i = 0; i < populationSize; i++)
        {
            string key = i.ToString();

            if (!_gameOver.TryGetValue(key, out bool isGameOver))
            {
                _gameOver.Add(key, false);
            }

            if (isGameOver)
            {
                continue;
            }

            if (!_gameStates.TryGetValue(key, out GameState? state))
            {
                _gameStates.Add(key, null);
            }

            state = UpdateBody(state);
            if (state.IsDead)
            {
                _gameOver[key] = true;
                _finalStates.Add(state);  // Saves the valid dead state (not null!)
                _gameStates[key] = state; // Keeps the body visible for the React UI

                continue;
            }

            state = UpdateFood(state);
            _gameStates[key] = state;
            allDead = false;
        }

        if (_bestPreviousBrain != null)
        {
            if (!_gameOver.TryGetValue(best, out bool isBestDead))
            {
                _gameOver.Add(best, false);
            }

            if (!isBestDead)
            {
                if (!_gameStates.TryGetValue(best, out GameState? bestState) || bestState == null)
                {
                    bestState = new GameState(
                        Body: [],
                        Food: new Point(-1, -1),
                        Health: width * heigth,
                        Brain: _bestPreviousBrain,
                        Visited: [],
                        FoodHistory: _bestFoodHistory,
                        IsReplay: true
                    );
                }

                bestState = UpdateBody(bestState);
                if (bestState.IsDead)
                {
                    _gameOver[best] = true;
                    _gameStates[best] = bestState;
                }
                else
                {
                    bestState = UpdateFood(bestState);
                    _gameStates[best] = bestState;
                }
            }
        }

        // 2. When everyone dies, flip the flag but DO NOT evolve yet!
        if (allDead)
        {
            _readyToEvolve = true;
        }

        // This now safely returns the graveyard!
        return _gameStates;
    }

    private void EvolveNewGeneration()
    {
        List<GameState> validStates = _finalStates.Where(s => s != null).ToList();

        GameState? champion = validStates.OrderByDescending(s => s.Points).FirstOrDefault();
        if (champion != null)
        {
            _bestPreviousBrain = champion.Brain;
            _bestFoodHistory = [.. champion.FoodHistory]; // <-- SAVE THE SCRIPT!
        }

        List<GameState> parents = [];
        for (int i = 0; i < numberOfParents; i++)
        {
            GameState? bestContender = null;
            for (int t = 0; t < tournamentSize; t++)
            {
                int randomIndex = Random.Shared.Next(validStates.Count);
                GameState contender = validStates[randomIndex]; // Use validStates here

                if (bestContender == null || contender.Points > bestContender.Points)
                {
                    bestContender = contender;
                }
            }
            parents.Add(bestContender!);
        }

        List<NeuralNetwork> nextGenerationBrains = [];
        var elites = validStates.OrderByDescending(s => s.Points).Take(elitismCount).ToList();

        foreach (var elite in elites)
        {
            // We add the exact brain without mutating it!
            nextGenerationBrains.Add(elite.Brain);
        }
        // --------------------

        // Generate the REST of the population via crossover and mutation
        // Notice the loop condition is now (populationSize - elitismCount)
        for (int i = 0; i < populationSize - elitismCount; i++)
        {
            GameState parentA = parents[Random.Shared.Next(parents.Count)];
            GameState parentB = parents[Random.Shared.Next(parents.Count)];

            NeuralNetwork babyBrain = parentA.Brain.CrossoverAndMutate(parentB.Brain, mutationRate: 0.05f);
            nextGenerationBrains.Add(babyBrain);
        }

        _finalStates.Clear();
        for (int i = 0; i < populationSize; i++)
        {
            string key = i.ToString();
            _gameOver[key] = false;

            _gameStates[key] = new GameState(
                Body: [],
                Food: new Point(-1, -1),
                Health: width * heigth,
                Brain: nextGenerationBrains[i],
                Visited: [],
                FoodHistory: [] // <--- ADD THIS HERE!
            );
        }

        _gameOver[best] = false;
        _gameStates[best] = null;

        gen++;
    }

    private GameState UpdateFood(GameState? state)
    {
        state ??= GetGameState();

        Point food = state.Food;
        if (food is { X: -1, Y: -1 })
        {
            // --- NEW: REPLAY LOGIC ---
            if (state.IsReplay && state.ReplayIndex < state.FoodHistory.Count)
            {
                // Replay Mode: Feed it the exact apple from the winning run
                food = state.FoodHistory[state.ReplayIndex];
                state = state with { ReplayIndex = state.ReplayIndex + 1 };
            }
            else
            {
                // Live Mode: Spawn random apple and record it
                food = PickLocation(state.Body);
                state.FoodHistory.Add(food);
            }
            // -------------------------

            Point head = state.Body.Count > 0 ? state.Body[^1] : new Point(0, 0);
            int exactDistance = GetTrueShortestPath(head, food, state.Body);
            int dynamicHealth = exactDistance + 50;

            state = state with { Food = food, Health = dynamicHealth };
        }

        return state;
    }

    private int GetTrueShortestPath(Point start, Point target, List<Point> body)
    {
        // 1. Setup the BFS Queue and Visited tracker
        Queue<(Point Position, int Distance)> queue = new();
        HashSet<Point> visited = [.. body]; // The body is solid obstacles

        // 4 directions (Up, Down, Left, Right)
        int[] dx = { 0, 1, 0, -1 };
        int[] dy = { -1, 0, 1, 0 };

        queue.Enqueue((start, 0));
        visited.Add(start);

        // 2. Flood outward until we find the food
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            // Did we reach the food?
            if (current.Position.X == target.X && current.Position.Y == target.Y)
            {
                return current.Distance;
            }

            // Check all 4 adjacent squares
            for (int i = 0; i < 4; i++)
            {
                int nx = current.Position.X + dx[i];
                int ny = current.Position.Y + dy[i];
                Point nextPoint = new(nx, ny);

                // If the square is on the board and NOT part of the snake's body
                if (nx >= 0 && nx < width && ny >= 0 && ny < heigth && !visited.Contains(nextPoint))
                {
                    visited.Add(nextPoint);
                    queue.Enqueue((nextPoint, current.Distance + 1));
                }
            }
        }

        // 3. Fallback: If no path is found (the food spawned inside a completely trapped circle of body parts)
        // We just return the standard Manhattan distance. The snake is doomed anyway.
        return GetDistance(start, target);
    }

    private static GameState GetGameState()
    {
        NeuralNetwork freshBrain = new();
        freshBrain.InitializeRandomWeights();

        return new GameState(
            Body: [],
            Food: new Point(-1, -1),
            Health: width * heigth,
            Brain: freshBrain,
            Visited: [],
            FoodHistory: []
        );
    }

    private Point PickLocation(List<Point> body)
    {
        HashSet<Point> taken = [.. body];
        List<Point> empty = [];

        for (int i = 0; i < width; i++)
        {
            for (int j = 0; j < heigth; j++)
            {
                Point p = new(i, j);
                if (taken.Contains(p))
                {
                    continue;
                }

                empty.Add(p);
            }
        }

        int index = Random.Shared.Next(empty.Count);
        return empty[index];
    }

    private GameState? UpdateBody(GameState? state)
    {
        state ??= GetGameState();
        List<Point> body = state.Body;

        if (body is not { Count: > 0 })
        {
            for (int i = 0; i < initialSize; i++)
            {
                body.Add(new Point(i, 0));
            }

            state = state with { Body = body };
        }

        Point head = body[^1];
        int initialDistance = GetDistance(head, state.Food);

        float[] vision = GetVision(head, body, state.Food);
        float[] decisions = state.Brain.Predict(vision);

        int dir = 0;
        float maxDecisionValue = decisions[0];
        for (int i = 1; i < 4; i++)
        {
            if (decisions[i] > maxDecisionValue)
            {
                maxDecisionValue = decisions[i];
                dir = i;
            }
        }

        Point newHead = head;
        switch (dir)
        {
            case 0: newHead = head with { X = head.X + 1 }; break; // Right
            case 1: newHead = head with { X = head.X - 1 }; break; // Left
            case 2: newHead = head with { Y = head.Y + 1 }; break; // Down
            case 3: newHead = head with { Y = head.Y - 1 }; break; // Up
        }

        if (!state.Visited.TryGetValue(newHead, out int visitCount))
        {
            visitCount = 0;
        }
        visitCount++;
        state.Visited[newHead] = visitCount;

        int pointsModifier = 0;
        if (visitCount == 3)
        {
            pointsModifier += pointForLooping;
            // REMOVE _finalStates.Add(...)
            // REMOVE return null;

            // Return the dead state properly!
            return state with { Points = state.Points + pointsModifier, IsDead = true };
        }

        if (newHead.X < 0 || newHead.X >= width || newHead.Y < 0 || newHead.Y >= heigth)
        {
            pointsModifier += deathPenalty;
            return state with { Points = state.Points + pointsModifier, IsDead = true };
        }

        if (body.Contains(newHead) && newHead != body[0])
        {
            pointsModifier += deathPenalty;
            return state with { Points = state.Points + pointsModifier, IsDead = true };
        }

        int currentHealth = state.Health - 1;
        if (currentHealth <= 0)
        {
            pointsModifier += deathPenalty;
            return state with { Points = state.Points + pointsModifier, IsDead = true };
        }

        body.Add(newHead);
        if (newHead.X == state.Food.X && newHead.Y == state.Food.Y)
        {
            // --- NEW: Exponential Apple Scoring ---
            int applesEaten = body.Count - initialSize;
            int dynamicAppleReward = eatenApplePoints + (applesEaten * 5000);

            state = state with
            {
                Food = new Point(-1, -1),
                // Notice Health is NOT set here! UpdateFood will set it using BFS.
                Points = state.Points + dynamicAppleReward + pointsModifier,
                Visited = []
            };
        }
        else
        {
            body.RemoveAt(0);
            if (state.Food.X != -1)
            {
                int finalDistance = GetDistance(newHead, state.Food);
                if (finalDistance < initialDistance)
                {
                    pointsModifier += rightDirectionPoints;
                }
                else
                {
                    pointsModifier += wrongDirectionPoints;
                }
            }

            state = state with { Health = currentHealth, Points = state.Points + pointsModifier };
        }

        return state;
    }
    private static float[] GetVision(Point head, List<Point> body, Point food)
    {
        float[] vision = new float[26];
        HashSet<Point> bodySet = [.. body];

        int[] dx = [0, 1, 1, 1, 0, -1, -1, -1];
        int[] dy = [-1, -1, 0, 1, 1, 1, 0, -1];

        for (int i = 0; i < 8; i++)
        {
            int x = head.X;
            int y = head.Y;
            float distance = 0;

            bool foundFood = false;
            bool foundTail = false;

            float distanceToFood = 0;
            float distanceToTail = 0;

            while (true)
            {
                x += dx[i];
                y += dy[i];
                distance++;

                if (x < 0 || x >= width || y < 0 || y >= heigth)
                {
                    int index = i * 3;
                    vision[index] = 1.0f / distance;
                    vision[index + 1] = foundFood ? 1.0f / distanceToFood : 0;
                    vision[index + 2] = foundTail ? 1.0f / distanceToTail : 0;
                    break;
                }

                if (!foundFood && x == food.X && y == food.Y)
                {
                    foundFood = true;
                    distanceToFood = distance;
                }

                if (!foundTail && bodySet.Contains(new Point(x, y)))
                {
                    foundTail = true;
                    distanceToTail = distance;
                }
            }
        }

        vision[24] = (float)(food.X - head.X) / width;
        vision[25] = (float)(food.Y - head.Y) / heigth;

        return vision;
    }

    private static int GetDistance(Point head, Point food)
    {
        return Math.Abs(head.X - food.X) + Math.Abs(head.Y - food.Y);
    }
}