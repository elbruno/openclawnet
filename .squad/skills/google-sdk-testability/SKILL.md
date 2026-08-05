# Google SDK Testability

## Problem

Generated Google .NET SDK resource classes (for example `UsersResource.MessagesResource`) are not reliable Moq targets because important members are non-virtual or otherwise difficult for Castle DynamicProxy to proxy.

## Pattern

Add a test seam at the transport layer instead of mocking generated resource classes:

1. Keep production code constructing real `GmailService`, `CalendarService`, or other `BaseClientService` subclasses.
2. Add an optional `HttpMessageHandler?` (or handler factory) to the owning client factory.
3. When provided, set `BaseClientService.Initializer.HttpClientFactory` to a `Google.Apis.Http.HttpClientFromMessageHandlerFactory` that wraps the handler.
4. For WireMock integration tests, also allow a test-only service base URI so generated SDK requests target the local server.
5. Route OAuth refresh HTTP through the same injected handler to keep token-refresh regressions hermetic and covered.

## Test shape

- Unit tests: use a small `HttpMessageHandler` stub and assert outgoing `RequestUri`, headers, and JSON results.
- Integration tests: use `WireMockServer`, pass a real `HttpClientHandler`, and set the Google service base URI to the WireMock URL.
- Avoid mocking generated SDK resources; let the Google SDK serialize, authorize, and execute requests normally.

## Production safety

The handler parameter must default to `null`. DI should pass `null` in production so Google SDK default transport behavior stays unchanged.
