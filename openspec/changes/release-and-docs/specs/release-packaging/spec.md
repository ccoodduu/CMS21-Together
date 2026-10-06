# Spec Delta

## Purpose

Defines what a CMS21-Together release consists of (a client zip and a server zip with exactly known contents and
instructions), how it is built from a commit, how it is checked before it is shared, and how players collect logs
for a bug report.

## ADDED Requirements

### Requirement: Client zip
A release SHALL include a client zip that, unzipped into the game folder of a MelonLoader 0.5.7 install, installs
the mod with Steam support and a copy of the dedicated server, and contains no test-only files.

#### Scenario: Fresh install
- **WHEN** a player unzips the client zip into a game folder that has MelonLoader 0.5.7 and starts the game
- **THEN** the mod loads with Steam features available and the dedicated server can be started from the
  `TogetherServer` folder

#### Scenario: No test files
- **WHEN** the client zip is inspected
- **THEN** it contains neither the test harness nor any other mod

### Requirement: Server zip
A release SHALL include a server zip that runs the dedicated server from any folder on Windows without the game
installed, and contains no configuration, logs or saves.

#### Scenario: Dedicated host
- **WHEN** a host unzips the server zip into an empty folder and starts the server
- **THEN** the server creates its default configuration and starts with Steam enabled

### Requirement: Checked contents
The build SHALL fail when a zip is missing an expected file, contains an unexpected one, or when the version built
into the mod differs from the version in the zip's name; each zip SHALL carry a manifest with the version, commit
and file hashes.

#### Scenario: Missing file
- **WHEN** a build produces a client zip without the Steam library
- **THEN** the build fails naming the missing file and no zip is left as a result

### Requirement: One build command
A single command SHALL build both zips from the current commit; dev builds SHALL refuse uncommitted changes unless
explicitly allowed and labelled as such.

#### Scenario: Dirty tree
- **WHEN** the build command runs with uncommitted changes and without the allow flag
- **THEN** it stops before building and lists the changed files

### Requirement: How-to-try text
Both zips SHALL contain a short text that tells a player how to install, host or join, what works in this build,
and where the logs are.

#### Scenario: Friend reads the zip only
- **WHEN** a friend opens the client zip
- **THEN** the try-it text in it names MelonLoader 0.5.7, the folder to unzip into, how to join a host and where the
  log files are

### Requirement: Release smoke test
Before a build is shared, it SHALL be installable into the two local test installs and the local server from the
zips alone, and two clients SHALL connect and reach the same state with it.

#### Scenario: Smoke run
- **WHEN** the release zips are installed into the test environment and the smoke scenario runs
- **THEN** both clients and the server report the zip's version and both clients reach the garage with equal state

### Requirement: Offline log collection
Both zips SHALL contain a log collector that, without the game running, packs the client and server logs and
version information into one archive for a bug report, leaves out the player's identity key, and replaces
passwords, admin keys and Steam tokens with a placeholder. Server saves SHALL be included only when asked for.

#### Scenario: After a crash
- **WHEN** the game crashed and the player runs the log collector from the game folder
- **THEN** one archive appears on the Desktop with the latest client logs, the local server's logs and the versions,
  and it contains no identity key and no password in clear text
