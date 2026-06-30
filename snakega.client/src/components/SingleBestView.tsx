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

            // If SignalR hasn't connected or sent data yet, just keep looping
            if (!data || !data.players) {
                animationFrameId = requestAnimationFrame(renderLoop);
                return;
            }

            ctx.clearRect(0, 0, canvas.width, canvas.height);
            const BLOCK_SIZE = 20;

            // Check if the "best" key exists, and what its value is
            const hasChampion = "best" in data.players;
            const gameState = data.players["best"];

            // Handle Generation 1 (No champion exists yet)
            if (!hasChampion || (data.generation === 1 && gameState === null)) {
                ctx.fillStyle = '#4b5563'; // Gray text
                ctx.font = '32px system-ui, sans-serif';
                ctx.textAlign = 'center';
                ctx.fillText('AWAITING CHAMPION...', canvas.width / 2, canvas.height / 2);

                ctx.font = '20px system-ui, sans-serif';
                ctx.fillText('Let Generation 1 finish training.', canvas.width / 2, (canvas.height / 2) + 40);

                animationFrameId = requestAnimationFrame(renderLoop);
                return;
            }

            // Handle Champion Death
            if (gameState === null) {
                ctx.fillStyle = 'rgba(239, 68, 68, 0.15)'; // Subtle red background
                ctx.fillRect(0, 0, canvas.width, canvas.height);

                ctx.fillStyle = '#ef4444'; // Bright red text
                ctx.font = 'bold 36px system-ui, sans-serif';
                ctx.textAlign = 'center';
                ctx.fillText('CHAMPION DIED', canvas.width / 2, canvas.height / 2);

                animationFrameId = requestAnimationFrame(renderLoop);
                return;
            }

            // ---------------------------------------------
            // Draw the Champion!
            // ---------------------------------------------

            // Draw Food (Red)
            ctx.fillStyle = '#ef4444';
            ctx.fillRect(gameState.food.x * BLOCK_SIZE, gameState.food.y * BLOCK_SIZE, BLOCK_SIZE, BLOCK_SIZE);

            // Draw Snake Body (Neon Green)
            ctx.fillStyle = '#10b981';
            gameState.body.forEach((segment) => {
                ctx.fillRect(segment.x * BLOCK_SIZE, segment.y * BLOCK_SIZE, BLOCK_SIZE, BLOCK_SIZE);
            });

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