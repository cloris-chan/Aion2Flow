# Complete stream fixtures

The active protocol epoch starts on 2026-07-01. Runtime parser regressions use
complete stream logs rather than individual packet hex fragments.

Steam fixtures are `aion2flow.stream.20261031060531.log` and all thirteen logs
dated `20261106`: `030558`, `030640`, `031049`, `031238`, `031351`, `031402`,
`031815`, `031937`, `032017`, `032038`, `032326`, `032510`, `032740`.
`SteamClientReplayTests` selects `ClientProfiles.Steam` explicitly. The operator
identified these clients as Steam; original app `session.json` files record the
old PURPLE selection and remain unchanged in Research snapshots. The fixtures
preserve the complete stream bytes, including lifecycle rows.

November source hashes, boundaries, entity selectors and ownership evidence are
recorded in the sibling Research repository at
`artifacts/protocol/steam-runtime-validation.20261106.json`. Source dumps do not
identify a game build or prove SYN-to-FIN TCP completeness. Bundled Steam
resources were freshly generated from the installed build at analysis time.

PURPLE controls remain explicit: `20261007155148` and `20261017050224` exercise
their original layouts, and Steam must not interpret their NPC state fields.
Existing earlier PURPLE scenarios retain their own replay expectations.

The thirteen November logs are one continuous play session, not independent
encounters. `SteamMapReplayTests` replays them in chronological order through
`ReplayMany`, retaining one transport connection across file boundaries. Expected
results include 13 map contexts, five same-map teleports, seven combat archives,
and final map 1010. Map 100011 remains valid without a bundled display name.
Archive playback must preserve each scene's map/instance, journal range, metrics
and summon owners. File boundaries never cause a runtime scene reset.
