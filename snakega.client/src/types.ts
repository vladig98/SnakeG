export interface Point {
    x: number;
    y: number;
}

export interface GameState {
    body: Point[];
    food: Point;
    health: number;
}

export interface SimulationTickPayload {
    generation: number;
    players: Record<string, GameState | null>;
}