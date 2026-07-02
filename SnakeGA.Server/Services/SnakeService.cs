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
    private const string best = "best";
    private int gen = 1;
    private bool _readyToEvolve = false;
    private List<Point> _bestFoodHistory = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        int fastForwardFrameCounter = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            bool isPaused = gen >= control.TargetGeneration && _readyToEvolve;
            if (isPaused)
            {
                await Task.Delay(100, stoppingToken);
                continue;
            }

            bool isFastForwarding = gen < control.TargetGeneration;
            Dictionary<string, GameState?> gameStates = GenerateNextGenerationData();

            if (isFastForwarding)
            {
                fastForwardFrameCounter++;
                if (fastForwardFrameCounter % 1000 == 0)
                {
                    await Task.Yield();
                }
            }
            else
            {
                FastForwardDto dto = new(Generation: gen, Players: gameStates);
                await hubContext.Clients.All.SendAsync("ReceiveTick", dto, stoppingToken);

                await Task.Delay(100, stoppingToken);
            }
        }
    }

    private Dictionary<string, GameState?> GenerateNextGenerationData()
    {
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

            if (_gameOver.TryGetValue(key, out bool isGameOver) && isGameOver)
            {
                continue;
            }

            if (!_gameStates.TryGetValue(key, out GameState? state))
            {
                _gameStates.Add(key, null);
            }

            state = UpdateBody(state) ?? throw new NullReferenceException("Something bad has happened");
            if (state.IsDead)
            {
                _gameOver[key] = true;
                _finalStates.Add(state);
                _gameStates[key] = state;

                continue;
            }

            state = UpdateFood(state);
            _gameStates[key] = state;
            allDead = false;
        }

        if (_bestPreviousBrain is not null)
        {
            bool isBestDead = _gameOver.TryGetValue(best, out bool dead) && dead;
            if (!isBestDead)
            {
                if (!_gameStates.TryGetValue(best, out GameState? bestState) || bestState is null)
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

                bestState = UpdateBody(bestState) ?? throw new NullReferenceException("Something bad has happened");
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

        if (allDead)
        {
            _readyToEvolve = true;
        }

        return _gameStates;
    }

    private void EvolveNewGeneration()
    {
        List<GameState> validStates = [.. _finalStates.Where(s => s is not null).OrderByDescending(s => s.Points)];
        GameState? champion = validStates.FirstOrDefault();

        if (champion is not null)
        {
            _bestPreviousBrain = champion.Brain;
            _bestFoodHistory = [.. champion.FoodHistory];

            control.BestBrain = champion.Brain;
        }

        List<GameState> parents = SelectParents(validStates);

        List<NeuralNetwork> nextGenerationBrains = GetNextGenBrains(validStates, parents);
        CreateOffsprings(nextGenerationBrains);

        _finalStates.Clear();
        _gameOver[best] = false;
        _gameStates[best] = null;

        gen++;
    }

    private void CreateOffsprings(List<NeuralNetwork> nextGenerationBrains)
    {
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
                FoodHistory: []
            );
        }
    }

    private List<NeuralNetwork> GetNextGenBrains(List<GameState> validStates, List<GameState> parents)
    {
        List<NeuralNetwork> nextGenerationBrains = [];

        if (control.InjectedBrain is not null)
        {
            nextGenerationBrains.Add(control.InjectedBrain);
            control.InjectedBrain = null;
        }

        IEnumerable<GameState> elites = validStates.Take(control.ElitismCount);
        foreach (GameState elite in elites)
        {
            nextGenerationBrains.Add(elite.Brain);
        }

        while (nextGenerationBrains.Count < populationSize)
        {
            GameState parentA = parents[Random.Shared.Next(parents.Count)];
            GameState parentB = parents[Random.Shared.Next(parents.Count)];

            NeuralNetwork babyBrain = parentA.Brain.CrossoverAndMutate(parentB.Brain, mutationRate: control.MutationRate);
            nextGenerationBrains.Add(babyBrain);
        }

        return nextGenerationBrains;
    }

    private List<GameState> SelectParents(List<GameState> validStates)
    {
        List<GameState> parents = [];
        for (int i = 0; i < control.NumberOfParents; i++)
        {
            GameState bestContender = TournamentSelection(validStates);
            parents.Add(bestContender);
        }

        return parents;
    }

    private GameState TournamentSelection(List<GameState> validStates)
    {
        GameState? bestContender = null;
        for (int t = 0; t < control.TournamentSize; t++)
        {
            int randomIndex = Random.Shared.Next(validStates.Count);
            GameState contender = validStates[randomIndex];

            if (bestContender is null || contender.Points > bestContender.Points)
            {
                bestContender = contender;
            }
        }

        // Sfaety check
        if (bestContender is null)
        {
            throw new NullReferenceException("Something has gone terribly wrong!");
        }

        return bestContender;
    }

    private GameState UpdateFood(GameState? state)
    {
        state ??= GetGameState();
        Point food = state.Food;

        if (food is { X: -1, Y: -1 })
        {
            if (state.IsReplay && state.ReplayIndex < state.FoodHistory.Count)
            {
                food = state.FoodHistory[state.ReplayIndex];
                state = state with { ReplayIndex = state.ReplayIndex + 1 };
            }
            else
            {
                food = PickLocation(state.Body);
                state.FoodHistory.Add(food);
            }

            Point head = state.Body.Count > 0 ? state.Body[^1] : new Point(0, 0);
            int exactDistance = GetTrueShortestPath(head, food, state.Body);
            int dynamicHealth = exactDistance + control.HealthOffset;

            state = state with { Food = food, Health = dynamicHealth };
        }

        return state;
    }

    private static int GetTrueShortestPath(Point start, Point food, List<Point> body)
    {
        Queue<ShortestPathDto> queue = new();
        HashSet<Point> visited = [.. body];

        ReadOnlySpan<int> dx = [ 0, 1, 0, -1 ];
        ReadOnlySpan<int> dy = [ -1, 0, 1, 0 ];

        queue.Enqueue(new ShortestPathDto(start, 0));
        visited.Add(start);

        while (queue.Count > 0)
        {
            ShortestPathDto dto = queue.Dequeue();
            if (HasEatenFood(dto.Position.X, dto.Position.Y, food))
            {
                return dto.Distance;
            }

            for (int i = 0; i < dx.Length; i++)
            {
                int nx = dto.Position.X + dx[i];
                int ny = dto.Position.Y + dy[i];

                Point nextPoint = new(nx, ny);
                if (!IsOutOfBounds(nx, ny) && !visited.Contains(nextPoint))
                {
                    visited.Add(nextPoint);
                    queue.Enqueue(new ShortestPathDto(nextPoint, dto.Distance + 1));
                }
            }
        }

        return GetDistance(start, food);
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

    private static Point PickLocation(List<Point> body)
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
        List<Point> body = GetBody(ref state);

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

        Point newHead = MoveHead(head, dir);
        if (!state.Visited.TryGetValue(newHead, out int visitCount))
        {
            visitCount = 0;
        }

        visitCount++;
        state.Visited[newHead] = visitCount;

        int pointsModifier = 0;
        int currentHealth = state.Health - 1;

        GameState? end = CheckForEndGame(visitCount, ref pointsModifier, state, newHead, body, currentHealth);

        if (end is not null)
        {
            return end;
        }

        body.Add(newHead);
        state = MoveSnake(state, body, initialDistance, newHead, pointsModifier, currentHealth);

        return state;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Point MoveHead(Point head, int dir)
    {
        Point newHead = head;
        switch (dir)
        {
            // Right
            case 0: newHead = head with { X = head.X + 1 }; break;
            // Left
            case 1: newHead = head with { X = head.X - 1 }; break;
            // Down
            case 2: newHead = head with { Y = head.Y + 1 }; break;
            // Up
            case 3: newHead = head with { Y = head.Y - 1 }; break;
        }

        return newHead;
    }

    private GameState? CheckForEndGame(int visitCount, ref int pointsModifier, GameState state, Point newHead, List<Point> body, int currentHealth)
    {
        if (visitCount == control.NumberOfRepeats)
        {
            pointsModifier += control.PointForLooping;
            return state with { Points = state.Points + pointsModifier, IsDead = true };
        }

        if (IsOutOfBounds(newHead.X, newHead.Y))
        {
            pointsModifier += control.DeathPenalty;
            return state with { Points = state.Points + pointsModifier, IsDead = true };
        }

        if (body.Contains(newHead) && newHead != body[0])
        {
            pointsModifier += control.DeathPenalty;
            return state with { Points = state.Points + pointsModifier, IsDead = true };
        }
        
        if (currentHealth <= 0)
        {
            pointsModifier += control.DeathPenalty;
            return state with { Points = state.Points + pointsModifier, IsDead = true };
        }

        return null;
    }

    private GameState MoveSnake(GameState state, List<Point> body, int initialDistance, Point newHead, int pointsModifier, int currentHealth)
    {
        if (HasEatenFood(newHead.X, newHead.Y, state.Food))
        {
            int applesEaten = body.Count - initialSize;
            int dynamicAppleReward = control.EatenApplePoints + (applesEaten * control.ExtraApplesMultiplier);

            state = state with
            {
                Food = new Point(-1, -1),
                Points = state.Points + dynamicAppleReward + pointsModifier,
                Visited = []
            };

            return state;
        }

        body.RemoveAt(0);
        int finalDistance = GetDistance(newHead, state.Food);

        int extaPoints = finalDistance < initialDistance ? control.RightDirectionPoints : control.WrongDirectionPoints;
        pointsModifier += extaPoints;

        state = state with { Health = currentHealth, Points = state.Points + pointsModifier };

        return state;
    }

    private static List<Point> GetBody(ref GameState state)
    {
        List<Point> body = state.Body;
        if (body is not { Count: > 0 })
        {
            for (int i = 0; i < initialSize; i++)
            {
                body.Add(new Point(i, 0));
            }

            state = state with { Body = body };
        }

        return body;
    }

    private static float[] GetVision(Point head, List<Point> body, Point food)
    {
        ReadOnlySpan<int> dx = [0, 1, 1, 1, 0, -1, -1, -1];
        ReadOnlySpan<int> dy = [-1, -1, 0, 1, 1, 1, 0, -1];

        int count = dx.Length;

        float[] vision = new float[count * 3 + 2];
        HashSet<Point> bodySet = [.. body];

        for (int i = 0; i < count; i++)
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

                if (IsOutOfBounds(x, y))
                {
                    int index = i * 3;

                    vision[index] = 1.0f / distance;
                    vision[index + 1] = foundFood ? 1.0f / distanceToFood : 0;
                    vision[index + 2] = foundTail ? 1.0f / distanceToTail : 0;

                    break;
                }

                if (!foundFood && HasEatenFood(x, y, food))
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool HasEatenFood(int x, int y, Point food)
    {
        return x == food.X && y == food.Y;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsOutOfBounds(int x, int y)
    {
        return x < 0 || x >= width || y < 0 || y >= heigth;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetDistance(Point head, Point food)
    {
        return Math.Abs(head.X - food.X) + Math.Abs(head.Y - food.Y);
    }
}