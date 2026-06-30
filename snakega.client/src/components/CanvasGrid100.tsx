import { useEffect, useRef } from 'react';
import type { SimulationTickPayload } from '../types';

interface Props {
    latestDataRef: React.RefObject<SimulationTickPayload | null>;
}

export default function CanvasGrid100({ latestDataRef }: Props) {
    const canvasRef = useRef<HTMLCanvasElement>(null);

    useEffect(() => {
        const canvas = canvasRef.current;
        if (!canvas) return;
        const ctx = canvas.getContext('2d');
        if (!ctx) return;

        let animationFrameId: number;

        const renderLoop = () => {
            const data = latestDataRef.current;
            if (!data) {
                animationFrameId = requestAnimationFrame(renderLoop);
                return;
            }

            ctx.clearRect(0, 0, canvas.width, canvas.height);

            // Syncing with backend: 60 width x 30 height
            const BLOCK_SIZE = 3;
            const SCREEN_WIDTH = 60 * BLOCK_SIZE;  // 180
            const SCREEN_HEIGHT = 30 * BLOCK_SIZE; // 90

            for (let i = 0; i < 100; i++) {
                const gameState = data.players[i.toString()];
                const col = i % 10;
                const row = Math.floor(i / 10);

                const startX = col * SCREEN_WIDTH;
                const startY = row * SCREEN_HEIGHT;

                ctx.strokeStyle = 'var(--border)';
                ctx.strokeRect(startX, startY, SCREEN_WIDTH, SCREEN_HEIGHT);

                if (gameState === null) {
                    ctx.fillStyle = 'rgba(255, 0, 0, 0.2)';
                    ctx.fillRect(startX, startY, SCREEN_WIDTH, SCREEN_HEIGHT);
                    continue;
                }

                ctx.fillStyle = 'red';
                ctx.fillRect(startX + (gameState.food.x * BLOCK_SIZE), startY + (gameState.food.y * BLOCK_SIZE), BLOCK_SIZE, BLOCK_SIZE);

                ctx.fillStyle = '#00FF00';
                gameState.body.forEach(segment => {
                    ctx.fillRect(startX + (segment.x * BLOCK_SIZE), startY + (segment.y * BLOCK_SIZE), BLOCK_SIZE, BLOCK_SIZE);
                });
            }

            animationFrameId = requestAnimationFrame(renderLoop);
        };

        renderLoop();
        return () => cancelAnimationFrame(animationFrameId);
    }, [latestDataRef]);

    // Width is 10 cols * 180. Height is 10 rows * 90.
    // Using max-w-full so it scales down on smaller monitors
    return <canvas
        ref={canvasRef}
        width={1800}
        height={900}
        style={{
            width: '100%',
            height: 'auto',
            display: 'block',
            backgroundColor: '#030712', // Pure dark background for the games
            borderRadius: '8px'
        }}
    />;
}