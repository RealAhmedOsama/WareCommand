# Hardware integration boundary

This document records the bounded Application-layer slice for issue #73. The
business services remain independent of browser APIs, native device APIs, and
vendor SDKs.

## Current slice

- `WmsScannerProfile` describes bounded length, prefix, terminator,
  inter-character timeout, and duplicate-suppression policy.
- `WmsScanEventNormalizer` and `WmsKeyboardWedgeBuffer` normalize keyboard
  wedge, camera, native-adapter, and manual input into a common event while
  distinguishing accepted, incomplete, duplicate, and rejected values.
- The normalizer does not resolve stock, locations, or orders. Parsed values
  still go through the existing server-side identification/GS1 validation
  and workflow authorization boundary.
- `WmsScaleProfile` and `WmsScaleStabilityAccumulator` require matching units,
  configured precision, non-negative values unless explicitly allowed, and a
  configured number of consecutive stable readings. Manual fallback is an
  explicit profile capability and is marked on the resulting weight.
- `WmsStationDeviceProfile` provides a typed station/warehouse boundary for
  scanner, scale, label-printer, document-printer, sound, and vibration
  capabilities. `WmsPrintRouteCatalog` deterministically prefers station and
  warehouse-specific routes without carrying endpoints or credentials.
- `IWmsScannerAdapter`, `IWmsScaleAdapter`, and `IWmsPrintAdapter` are the
  vendor-neutral adapter seams. No proprietary SDK dependency was added to
  Domain or Application.

## Safety rules

- Hardware input is a hint until the server validates the value and current
  warehouse/workflow state.
- Duplicate suppression is a UI/device-session convenience, not an inventory
  idempotency guarantee. Mutating commands continue to use their server-side
  idempotency contracts.
- Scale readings are session memory only; they are not persisted as accepted
  inventory facts by this slice.
- Adapter keys are identifiers only. Secret-bearing connection settings remain
  host/deployment concerns.

## Remaining issue #73 gates

This is progress only. Closure still requires browser camera permission and
unsupported-device fallback, persistent-focus/virtual-keyboard integration on
each operator screen, station-profile persistence and administration, real
adapter implementations, scale calibration/diagnostics, print payload and
template execution, permission/audit controls for device access, and
automated handheld/browser qualification for rapid, partial, duplicate,
camera, scale, and print flows.
