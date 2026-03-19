JwtAuthStub
-----------

Simple Azure Functions project that returns a symmetric-signed JWT for testing/development. Replace with secure auth flow in production.

Environment variables:
- `JWT_SIGNING_KEY` - secret key for HMAC signing (dev only). Replace in production with secure key vault reference.
