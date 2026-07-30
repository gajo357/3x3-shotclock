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
        @font-face {
            font-family: "United Sans Reg";
            src: url("/assets/united-sans-reg-bold.otf") format("opentype");
            font-style: normal;
            font-weight: 700;
            font-display: swap;
        }

        html, body {
            width: 100%;
            height: 100%;
            margin: 0;
            overflow: hidden;
            background: transparent;
            font-family: "United Sans Reg", "Arial Narrow", sans-serif;
        }

        .scoreboard {
            position: absolute;
            left: 50%;
            bottom: 55px;
            transform: translateX(-50%);
            display: grid;
            grid-template-columns: 250px 92px 170px 92px 250px;
            column-gap: 14px;
            align-items: center;
            min-height: 112px;
            padding: 12px 20px;
            box-sizing: border-box;
            color: white;
            background: rgba(10, 10, 14, 0.94);
            border-radius: 4px;
        }

        .team-details {
            min-width: 0;
        }

        .team {
            overflow: hidden;
            font-size: 30px;
            font-weight: 700;
            line-height: 1;
            text-overflow: ellipsis;
            text-transform: uppercase;
            white-space: nowrap;
        }

        .team.away,
        .away-details {
            text-align: right;
        }

        .score-box {
            display: grid;
            width: 84px;
            height: 84px;
            place-items: center;
            border: 3px solid #ffffff;
            border-radius: 3px;
            box-sizing: border-box;
            font-size: 58px;
            font-weight: 700;
            line-height: 1;
            text-align: center;
        }

        .clocks {
            text-align: center;
        }

        .game-clock {
            font-size: 38px;
            font-weight: 700;
            line-height: 1;
        }

        .shot-clock {
            margin-top: 5px;
            font-size: 24px;
            font-weight: 700;
            line-height: 1;
        }

        .foul-details {
            --foul-color: #ffffff;
            display: flex;
            gap: 8px;
            align-items: center;
            margin-top: 9px;
            color: var(--foul-color);
        }

        .away-details .foul-details {
            flex-direction: row-reverse;
        }

        .foul-label {
            font-size: 16px;
            font-weight: 700;
            line-height: 1;
        }

        .foul-box {
            display: grid;
            width: 42px;
            height: 42px;
            place-items: center;
            border: 2px solid currentColor;
            border-radius: 2px;
            box-sizing: border-box;
            font-size: 27px;
            font-weight: 700;
            line-height: 1;
        }
    </style>
</head>
<body>
    <div class="scoreboard">
        <div class="team-details">
            <div id="homeTeam" class="team">HOME</div>
            <div id="homeFoulDetails" class="foul-details">
                <span class="foul-label">FOULS</span>
                <span class="foul-box"><span id="homeFouls">0</span></span>
            </div>
        </div>
        <div id="homeScore" class="score-box">0</div>
        <div class="clocks">
            <div id="gameClock" class="game-clock">10:00</div>
            <div id="shotClock" class="shot-clock">12</div>
        </div>
        <div id="awayScore" class="score-box">0</div>
        <div class="team-details away-details">
            <div id="awayTeam" class="team away">AWAY</div>
            <div id="awayFoulDetails" class="foul-details">
                <span class="foul-label">FOULS</span>
                <span class="foul-box"><span id="awayFouls">0</span></span>
            </div>
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
            document.getElementById("homeFoulDetails").style.setProperty(
                "--foul-color",
                state.homeFoulColorHex || "#FFFFFF");
            document.getElementById("awayFoulDetails").style.setProperty(
                "--foul-color",
                state.awayFoulColorHex || "#FFFFFF");
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
