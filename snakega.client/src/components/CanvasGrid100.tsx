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

            const BLOCK_SIZE = 3;
            const SCREEN_WIDTH = 60 * BLOCK_SIZE;  // 180
            const SCREEN_HEIGHT = 30 * BLOCK_SIZE; // 90

            for (let i = 0; i < 100; i++) {
                const gameState = data.players[i.toString()];
                const col = i % 10;
                const row = Math.floor(i / 10);

                const startX = col * SCREEN_WIDTH;
                const startY = row * SCREEN_HEIGHT;

                // Draw Border
                ctx.strokeStyle = '#1f2937'; // Dark gray border
                ctx.strokeRect(startX, startY, SCREEN_WIDTH, SCREEN_HEIGHT);

                // Handle Dead Snake
                if (gameState.isDead) {
                    ctx.fillStyle = 'rgba(239, 68, 68, 0.15)'; // Red tint for dead grids
                    ctx.fillRect(startX, startY, SCREEN_WIDTH, SCREEN_HEIGHT);

                    // Draw the dead body in gray so you can see how it crashed
                    ctx.fillStyle = 'rgba(156, 163, 175, 0.5)';
                    gameState.body.forEach((segment: { x: number, y: number }) => {
                        ctx.fillRect(startX + (segment.x * BLOCK_SIZE), startY + (segment.y * BLOCK_SIZE), BLOCK_SIZE, BLOCK_SIZE);
                    });
                } else {
                    // Draw Living Food
                    ctx.fillStyle = '#ef4444';
                    ctx.fillRect(startX + (gameState.food.x * BLOCK_SIZE), startY + (gameState.food.y * BLOCK_SIZE), BLOCK_SIZE, BLOCK_SIZE);

                    // Draw Living Snake Body
                    ctx.fillStyle = '#10b981';
                    gameState.body.forEach((segment: { x: number, y: number }) => {
                        ctx.fillRect(startX + (segment.x * BLOCK_SIZE), startY + (segment.y * BLOCK_SIZE), BLOCK_SIZE, BLOCK_SIZE);
                    });
                }

                ctx.fillStyle = gameState.isDead ? 'rgba(255, 255, 255, 0.3)' : 'rgba(255, 255, 255, 0.9)';
                ctx.font = '12px system-ui, sans-serif';

                ctx.textAlign = 'left';
                ctx.fillText(`#${i}`, startX + 6, startY + 16);

                ctx.textAlign = 'right';
                ctx.fillText(`${gameState.points}`, startX + SCREEN_WIDTH - 6, startY + 16);

                // Draw Food
                ctx.fillStyle = '#ef4444';
                ctx.fillRect(startX + (gameState.food.x * BLOCK_SIZE), startY + (gameState.food.y * BLOCK_SIZE), BLOCK_SIZE, BLOCK_SIZE);

                // Draw Snake Body
                ctx.fillStyle = '#10b981';
                gameState.body.forEach((segment: { x: number, y: number }) => {
                    ctx.fillRect(startX + (segment.x * BLOCK_SIZE), startY + (segment.y * BLOCK_SIZE), BLOCK_SIZE, BLOCK_SIZE);
                });

                // --- NEW: DRAW TEXT HUD FOR LIVING SNAKE ---
                ctx.fillStyle = 'rgba(255, 255, 255, 0.8)';
                ctx.font = '12px system-ui, sans-serif';

                // Draw Index (Top Left)
                ctx.textAlign = 'left';
                ctx.fillText(`#${i}`, startX + 6, startY + 16);

                // Draw Score (Top Right)
                ctx.textAlign = 'right';
                ctx.fillText(`${gameState.points}`, startX + SCREEN_WIDTH - 6, startY + 16);
            }

            animationFrameId = requestAnimationFrame(renderLoop);
        };

        renderLoop();
        return () => cancelAnimationFrame(animationFrameId);
    }, [latestDataRef]);

    return (
        <canvas
            ref={canvasRef}
            width={1800}
            height={900}
            style={{
                width: '100%',
                height: 'auto',
                display: 'block',
                backgroundColor: '#030712',
                borderRadius: '8px'
            }}
        />
    );
}