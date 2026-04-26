# Scribble Showdown
Submitted to TigerVerse 2026 by Matthew Martin, Alvin Moy, and Sayam Gupta

## Inspiration
We were inspired by both our previous project at TigerVerse 2025, ARtist, and also by the Pokémon franchise. ARtist showed us how cool it is to bring drawings into 3D space, and Pokémon got us thinking about what would happen if those drawings could battle each other!
## What it does
You strap on a Quest, draw a creature on the connected web app, and a couple minutes later it hatches into a 3D model in front of you in mixed reality. While it's generating, you pick a few personality traits, which ElevenLabs uses to give it its own voice. Once both players have drawn their scribbles, the battle starts. You can switch between trainer mode (stand still and shout move names like "thunderbolt" to attack) and scribble mode (you become the scribble and dodge attacks with the joystick). Each scribble gets a 4-move set with elemental matchups based on what was drawn.
## How we built it
The Quest app is Unity with the XR Interaction Toolkit. Multiplayer runs on Photon Fusion. Voice attacks go through Whisper for transcription and then fuzzy-match against the scribble's moveset. The 3D models come from Meshy.ai's image-to-3D pipeline, the personality-driven voices come from ElevenLabs, and Llama on Groq handles the reasoning for stuff like move generation and flavor text. The drawing canvas itself is a separate web app built with Astro and React, served alongside an API that talks to all the other services.
## Challenges we ran into
We ran into many Quest Link issues...
## Accomplishments that we're proud of
Collaborating with Meshy.ai to receive sponsored credits and pro plan. Also, the whole stack came together across Unity, web, and four different AI services without falling apart.
## What we learned
We learned how to make a multiplayer VR game built in Unity - basically a ton about Photon Fusion's network architecture.
## What's next for Scribble Showdown
Right now this is more of a showcase than a game. We want to build out an actual story mode, more elemental types, balance the existing moves so battles feel less random, and maybe a co-op drawing mode where two players collaborate on one creature.

The AI models/services we utilized were Claude, ChatGPT, Meshy.ai, ElevenLabs, and Groq.
