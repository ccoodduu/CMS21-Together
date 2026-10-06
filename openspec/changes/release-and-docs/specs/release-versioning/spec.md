# Spec Delta

## Purpose

Defines where the mod's version comes from, how release and development builds are labelled so that players can
tell builds apart, when the version number changes, and what a release must document.

## ADDED Requirements

### Requirement: Single version source
The client, the server and the release file names SHALL take their version from one place in the repository, and the
connection handshake SHALL compare that same value on both sides.

#### Scenario: Version shown everywhere
- **WHEN** build 0.6.0-dev.12 is installed
- **THEN** the client log, the server log and both zip names show 0.6.0-dev.12

### Requirement: Build labels
A release build SHALL carry the plain version; a development build SHALL carry the version plus `-dev.<n>`, where n
grows with every commit since the last release; a local developer build SHALL be labelled `-local`. The full version
in logs SHALL also name the commit.

#### Scenario: Two dev builds
- **WHEN** a friend runs 0.6.0-dev.12 and the host's server runs 0.6.0-dev.13
- **THEN** the friend's join is refused with a version-mismatch message naming both versions

### Requirement: Version bump policy
Before 1.0 each milestone release SHALL increase the minor version and a fix release of the same milestone the patch
version; 1.0.0 SHALL be the M6 release. The version SHALL be bumped to the next planned version in the first commit
after a release.

#### Scenario: After the M1 release
- **WHEN** 0.6.0 has been released and the next commit is made
- **THEN** that commit sets the version to 0.7.0, so later dev builds are 0.7.0-dev.n

### Requirement: Changelog for releases
A release build SHALL be refused unless the changelog has a section for its version and the commit is tagged with
that version.

#### Scenario: Release without changelog
- **WHEN** a release build of 1.0.0 is started and the changelog has no 1.0.0 section
- **THEN** the build stops and says that the changelog entry is missing
