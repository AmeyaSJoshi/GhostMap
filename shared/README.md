# Shared

| Folder | What it is |
| --- | --- |
| `com.ghostmap.shared/` | The Unity package both apps reference. The only source of truth for the scene schema, geometry, validation and protocol (`AGENTS.md` rule 3) |
| `TestProject/` | A minimal Unity project whose only job is running the package's EditMode tests. See its README |

Package layout:

```text
com.ghostmap.shared/
├── Runtime/
│   ├── Domain/       scene schema v1 DTOs, ValidationResult, WallDefinition
│   ├── Geometry/     coordinate frame, ray/plane, room and wall geometry, measurement
│   ├── Validation/   room, opening and furniture rules
│   └── Protocol/     protocol v1 constants, messages, serializer, revision policy
└── Tests/Editor/     156 EditMode tests
```

Both apps consume it from their `Packages/manifest.json` as
`"com.ghostmap.shared": "file:../../../shared/com.ghostmap.shared"` and list it
under `testables`, so each app's suite also runs the shared tests.

Run the tests with `./tools/run_unity_tests.sh shared`. Changing anything here
is a contract change if it touches the schema, protocol or validation limits:
see `AGENTS.md` rule 8 and plan section 27.
