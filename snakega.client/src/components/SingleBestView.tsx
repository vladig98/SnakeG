import { useEffect, useRef } from 'react';
import type { SimulationTickPayload } from '../types';

interface Props {
    latestDataRef: React.RefObject<SimulationTickPayload | null>;
}

export default function SingleBestView({ latestDataRef }: Props) {
    const canvasRef = useRef<HTMLCanvasElement>(null);

    useEffect(() => {
        const canvas = canvasRef.current;
        if (!canvas) return;
        const ctx = canvas.getContext('2d');
        if (!ctx) return;

        let animationFrameId: number;

        const renderLoop = () => {
            const data = latestDataRef.current;

            if (!data || !data.players) {
                animationFrameId = requestAnimationFrame(renderLoop);
                return;
            }

            ctx.clearRect(0, 0, canvas.width, canvas.height);
            const BLOCK_SIZE = 20;

            const hasChampion = "best" in data.players;
            const gameState = data.players["best"];

            if (!hasChampion || (data.generation === 1 && gameState === null)) {
                ctx.fillStyle = '#4b5563';
                ctx.font = '32px system-ui, sans-serif';
                ctx.textAlign = 'center';
                ctx.fillText('AWAITING CHAMPION...', canvas.width / 2, canvas.height / 2);

                animationFrameId = requestAnimationFrame(renderLoop);
                return;
            }

            if (gameState === null) {
                ctx.fillStyle = 'rgba(239, 68, 68, 0.15)';
                ctx.fillRect(0, 0, canvas.width, canvas.height);

                ctx.fillStyle = '#ef4444';
                ctx.font = 'bold 36px system-ui, sans-serif';
                ctx.textAlign = 'center';
                ctx.fillText('CHAMPION DIED', canvas.width / 2, canvas.height / 2);

                animationFrameId = requestAnimationFrame(renderLoop);
                return;
            }

            // Draw Food
            ctx.fillStyle = '#ef4444';
            ctx.fillRect(gameState.food.x * BLOCK_SIZE, gameState.food.y * BLOCK_SIZE, BLOCK_SIZE, BLOCK_SIZE);

            // Draw Snake Body
            ctx.fillStyle = '#10b981';
            gameState.body.forEach((segment: { x: number, y: number }) => {
                ctx.fillRect(segment.x * BLOCK_SIZE, segment.y * BLOCK_SIZE, BLOCK_SIZE, BLOCK_SIZE);
            });

            // --- NEW: DRAW CHAMPION HUD ---
            ctx.textAlign = 'left';

            // Generation Text
            ctx.fillStyle = 'rgba(255, 255, 255, 0.5)';
            ctx.font = '20px system-ui, sans-serif';
            ctx.fillText(`Generation ${data.generation}`, 20, 36);

            // Score Text
            ctx.fillStyle = 'rgba(255, 255, 255, 0.9)';
            ctx.font = 'bold 32px system-ui, sans-serif';
            ctx.fillText(`Score: ${gameState.points.toLocaleString()}`, 20, 72);

            animationFrameId = requestAnimationFrame(renderLoop);
        };

        renderLoop();
        return () => cancelAnimationFrame(animationFrameId);
    }, [latestDataRef]);

    return (
        <canvas
            ref={canvasRef}
            width={1200}
            height={600}
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