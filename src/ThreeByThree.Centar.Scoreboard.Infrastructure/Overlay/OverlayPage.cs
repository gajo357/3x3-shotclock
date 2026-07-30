namespace ThreeByThree.Centar.Scoreboard.Infrastructure.Overlay;

internal static class OverlayPage
{
    public const string Html = """
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>3x3 Centar Scoreboard Overlay</title>
    <style>
        html, body {
            width: 100%;
            height: 100%;
            margin: 0;
            overflow: hidden;
            background: transparent;
            font-family: Arial, sans-serif;
        }

        .scoreboard {
            position: absolute;
            left: 50%;
            bottom: 55px;
            transform: translateX(-50%);
            display: grid;
            grid-template-columns: 260px 90px 150px 90px 260px;
            align-items: center;
            min-height: 90px;
            padding: 10px 18px;
            box-sizing: border-box;
            color: white;
            background: rgba(10, 10, 14, 0.94);
            border-radius: 12px;
        }

        .team {
            overflow: hidden;
            font-size: 28px;
            font-weight: 700;
            text-overflow: ellipsis;
            text-transform: uppercase;
            white-space: nowrap;
        }

        .team.away,
        .away-details {
            text-align: right;
        }

        .score {
            font-size: 46px;
            font-weight: 900;
            text-align: center;
        }

        .clocks {
            text-align: center;
        }

        .game-clock {
            font-size: 34px;
            font-weight: 800;
        }

        .shot-clock {
            font-size: 22px;
            font-weight: 800;
        }

        .fouls {
            margin-top: 4px;
            font-size: 15px;
            opacity: 0.8;
        }
    </style>
</head>
<body>
    <div class="scoreboard">
        <div>
            <div id="homeTeam" class="team">HOME</div>
            <div class="fouls">Fouls: <span id="homeFouls">0</span></div>
        </div>
        <div id="homeScore" class="score">0</div>
        <div class="clocks">
            <div id="gameClock" class="game-clock">10:00</div>
            <div id="shotClock" class="shot-clock">12</div>
        </div>
        <div id="awayScore" class="score">0</div>
        <div class="away-details">
            <div id="awayTeam" class="team away">AWAY</div>
            <div class="fouls">Fouls: <span id="awayFouls">0</span></div>
        </div>
    </div>

    <script>
        function render(state) {
            document.getElementById("homeTeam").textContent = state.homeTeam;
            document.getElementById("awayTeam").textContent = state.awayTeam;
            document.getElementById("homeScore").textContent = state.homeScore;
            document.getElementById("awayScore").textContent = state.awayScore;
            document.getElementById("homeFouls").textContent = state.homeFouls;
            document.getElementById("awayFouls").textContent = state.awayFouls;
            document.getElementById("gameClock").textContent = state.gameClock;
            document.getElementById("shotClock").textContent = state.shotClock;
        }

        async function start() {
            try {
                const response = await fetch("/state", { cache: "no-store" });
                render(await response.json());
            } catch (error) {
                console.error("Failed to load the initial scoreboard state.", error);
            }

            const events = new EventSource("/events");
            events.onmessage = event => render(JSON.parse(event.data));
            events.onerror = error =>
                console.error("The scoreboard overlay connection was interrupted.", error);
        }

        start();
    </script>
</body>
</html>
""";
}
