# Launcher builds

`version.txt` is the **published launcher version**, read by the launcher's own updater. It is a
plain integer and must stay one: `SelfUpdate.ShouldSelfUpdate` treats anything unparseable as
"nothing to do", so a stray word here cannot start a self-replacement — but it also means a typo
silently stops updates rather than failing loudly.

⚠ **The executable itself is NOT kept here.** A launcher build is ~9 MB and committing one per
release would live in the repository's history forever, for every version, and binaries do not
delta-compress. Builds belong on GitHub Releases; this file exists so the updater can answer
"are you current?" without a download.

Bump the number here only when a new build is actually published, or the launcher will offer an
update it cannot fetch.
