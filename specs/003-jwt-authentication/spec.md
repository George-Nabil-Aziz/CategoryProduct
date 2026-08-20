# Feature Specification: JWT Authentication

**Feature Branch**: `003-jwt-authentication`

**Created**: 2026-08-20

**Status**: Draft

**Input**: User description: "I want to add JWT Authentication to CategoryProduct.API."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Log in to obtain access (Priority: P1)

A consumer with a pre-provisioned account submits their username and password and receives an
access credential (token) they can use on subsequent requests to prove who they are, without
re-sending their password every time.

**Why this priority**: This is the mechanism that turns a pre-provisioned account into usable
access — without it, no protected endpoint can ever be reached.

**Independent Test**: Can be fully tested by logging in with a known valid username/password pair
(for a pre-provisioned account) and confirming a usable access credential is returned, and
separately confirming an incorrect password or unknown username is rejected without issuing a
credential.

**Acceptance Scenarios**:

1. **Given** a pre-provisioned account, **When** the consumer logs in with the correct username
   and password, **Then** the system returns an access credential that identifies them as that
   user.
2. **Given** a pre-provisioned account, **When** the consumer logs in with an incorrect password,
   **Then** the login is rejected and no access credential is issued.
3. **Given** no account exists for a username, **When** a login is attempted with that username,
   **Then** the login is rejected and no access credential is issued, using the same generic
   failure response as a wrong password (so consumers cannot tell which accounts exist).

---

### User Story 2 - Protected write access to Category and Product data (Priority: P1)

An API consumer can only create, update, or delete Category or Product data by presenting a valid
access credential obtained from User Story 1. Write requests without one, or with an invalid or
expired one, are turned away before any data is changed. Reading Category or Product data remains
open to anyone, exactly as it is today.

**Why this priority**: This is the actual point of adding authentication — without enforcement,
login would exist but protect nothing, and Category/Product data could still be changed by anyone.

**Independent Test**: Can be fully tested by calling any Category or Product create, update, or
delete operation with no credential, with a made-up/invalid credential, and with a valid one
obtained from User Story 1, and confirming only the last case is allowed through to the normal
behavior — while confirming read access continues to work in all cases, with or without a
credential.

**Acceptance Scenarios**:

1. **Given** no access credential is presented, **When** a Category or Product create, update, or
   delete (POST/PUT/DELETE) endpoint is called, **Then** the request is rejected and no data is
   modified.
2. **Given** an invalid, malformed, or expired access credential is presented, **When** a Category
   or Product create, update, or delete (POST/PUT/DELETE) endpoint is called, **Then** the request
   is rejected and no data is modified.
3. **Given** a valid, unexpired access credential is presented, **When** a Category or Product
   create, update, or delete (POST/PUT/DELETE) endpoint is called, **Then** the request proceeds
   exactly as it did before this feature (same success behavior, same response shape).
4. **Given** no access credential is presented, **When** a Category or Product read (GET) endpoint
   is called, **Then** the request succeeds exactly as it did before this feature — read access is
   unaffected by this feature.

---

### Edge Cases

- What happens when a consumer presents an access credential belonging to an account that no
  longer exists (e.g., removed after the credential was issued)? The request MUST be rejected the
  same as an invalid credential.
- What happens when an access credential's validity period has simply run out, as opposed to being
  malformed? Both MUST be rejected identically from the consumer's point of view (generic
  "unauthorized," no distinction that would help an attacker).
- What happens to the existing, currently public Category and Product write endpoints (POST/PUT/
  DELETE) the moment this feature ships? They immediately require a valid access credential; `GET`
  (read) endpoints remain public and are entirely unaffected. Any existing consumer (including
  CategoryProduct.MVC) that performs writes without a credential will start receiving rejections
  until it is updated to authenticate first; consumers that only read are unaffected.
- What happens on repeated failed login attempts for the same account? Out of scope for this
  feature (see Assumptions) — no lockout/throttling behavior is required here.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST NEVER store, log, or transmit a consumer's password in plain text; only
  a securely hashed form MUST ever be persisted.
- **FR-002**: System MUST allow a consumer with a pre-provisioned account to log in with their
  username and password and, on success, MUST issue an access credential that identifies that
  consumer for later requests.
- **FR-003**: System MUST reject a login attempt with an incorrect password or an unknown
  username, in both cases using the same generic failure response, and MUST NOT issue an access
  credential for a rejected attempt.
- **FR-004**: System MUST require a valid, unexpired access credential on every create, update, or
  delete (POST/PUT/DELETE) request to any Category or Product endpoint, and MUST reject any such
  request that lacks one or presents an invalid/expired one, without performing the requested
  operation. Read (GET) requests to Category or Product endpoints remain unaffected and MUST NOT
  require a credential.
- **FR-005**: System MUST issue access credentials that expire after a limited period rather than
  remaining valid indefinitely (see Assumptions for the assumed default period).
- **FR-006**: System MUST NOT change the success-path behavior (response content or shape) of any
  existing Category or Product endpoint — only the addition of a credential requirement on write
  operations is in scope.
- **FR-007**: System MUST identify which consumer made a request from their access credential,
  without requiring the username/password to be re-sent on every request.

### Key Entities

- **Account**: A consumer's login identity, pre-provisioned directly in the system (see
  Assumptions) rather than through self-service sign-up. Attributes: unique username
  (case-insensitive), a securely hashed password. One account is used to obtain any number of
  access credentials over time.
- **Access Credential (token)**: Proof, issued at login, that a request comes from a specific,
  already-authenticated Account. Carries the account's identity and an expiration point; is
  presented on every subsequent write request to a protected endpoint in place of the
  username/password.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of create, update, or delete (POST/PUT/DELETE) requests to a Category or
  Product without a valid access credential are rejected, and 0 such requests result in data being
  changed; read (`GET`) requests continue to succeed with no credential required, exactly as
  before.
- **SC-002**: A consumer with a pre-provisioned account can log in and successfully perform a
  previously public Category/Product write operation using only the credential from login, with
  no other change in steps required, while read operations continue to work exactly as before with
  no credential needed.
- **SC-003**: 0 consumer passwords can be found stored anywhere in plain text after this feature
  ships.
- **SC-004**: An access credential that has passed its expiration point is rejected on 100% of
  attempts to use it, with no exceptions.

## Assumptions

- **Protection scope**: only the create, update, and delete (`POST`/`PUT`/`DELETE`) operations on
  Category and Product endpoints require a valid access credential. Read (`GET`) endpoints remain
  fully public, unchanged from today's behavior.
- **Accounts are pre-provisioned**: this feature does not include a self-service registration
  endpoint. Accounts are assumed to already exist in the system — created by some administrative
  or data-seeding process outside the scope of this feature — before any consumer attempts to log
  in. Login and write-endpoint protection are the full extent of this feature.
- **A single access level**: every authenticated account has identical access to every Category
  and Product endpoint; there is no admin/regular-user distinction in this feature. Rationale:
  no differing permission need was stated, and the project's guidance favors the smallest rule that
  satisfies the request — role-based permissions can be layered on later if a real need emerges.
- **Default credential lifetime**: an access credential is assumed valid for a limited session-like
  period (illustratively, on the order of an hour) rather than indefinitely; the exact duration is
  a planning-phase decision, not a business requirement.
- **No account-recovery or lockout behavior**: password reset, account recovery, and
  lockout/throttling after repeated failed logins are out of scope for this feature and may be
  addressed later if needed.
- **No credential revocation/logout endpoint**: because credentials expire on their own (FR-005),
  this feature does not require a way to invalidate a still-valid credential early (e.g., a
  server-side logout). A consumer simply discards the credential when done.
- **Existing Category/Product response content is unaffected**: this feature only adds a
  credential requirement in front of existing create/update/delete endpoints; it does not change
  what a successful response contains, and read endpoints are entirely unaffected.
