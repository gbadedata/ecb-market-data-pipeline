# ECB FX data contract

## Source contract

The extractor requests the ECB `EXR` dataflow through the ECB Data Portal SDMX 2.1 REST API.
Each request uses a five-part series key:

`D.{quote currency}.EUR.SP00.A`

For example, `D.GBP.EUR.SP00.A` represents the daily ECB reference rate for pounds sterling against the euro. The observation is interpreted as units of the quote currency per one euro.

The pipeline requires these response columns:

| ECB column | Canonical field | Rule |
| --- | --- | --- |
| `TIME_PERIOD` | `observation_date` | ISO date in `YYYY-MM-DD` format |
| `CURRENCY_DENOM` | `base_currency` | Must be `EUR` |
| `CURRENCY` | `quote_currency` | Must be one of the requested ISO currency codes |
| `OBS_VALUE` | `rate` | Invariant-culture decimal greater than zero |
| `OBS_STATUS` | `status` | Optional; defaults to `A` |
| `KEY` | `source_series` | Optional; reconstructed from the series dimensions if absent |

Any missing required column, malformed date, malformed decimal or inconsistent row width fails the run instead of being silently dropped.

## Canonical output contract

The processed CSV contains one row for each `observation_date + base_currency + quote_currency` business key.

| Field | Type | Meaning |
| --- | --- | --- |
| `observation_date` | date | ECB observation date |
| `base_currency` | string | Always `EUR` for this project |
| `quote_currency` | string | Requested currency, such as `GBP` |
| `rate` | decimal | Units of quote currency per one euro |
| `status` | string | ECB observation status |
| `source_series` | string | ECB series key used for traceability |
| `ingested_at_utc` | timestamp | UTC extraction timestamp |

The output is sorted by observation date and quote currency. Repeated ingestion updates the same business key and does not append a duplicate.

## Quality rules

Errors block updates to the processed dataset. Warnings are retained in the quality report but do not block publication.

| Rule | Severity | Purpose |
| --- | --- | --- |
| `base_currency` | Error | Rejects non-EUR series |
| `requested_currency` | Error | Prevents unrequested currencies entering the output |
| `positive_rate` | Error | Rejects zero or negative rates |
| `requested_date_range` | Error | Rejects observations outside the extraction window |
| `unique_business_key` | Error | Prevents duplicate date-currency records |
| `currency_present` | Error | Requires at least one observation per requested currency |
| `freshness` | Error | Rejects a latest observation beyond the configured staleness allowance |
| `weekday_coverage` | Warning | Flags coverage below 90%; ECB holidays can produce legitimate gaps |

## Provenance

Every run keeps the exact raw response, request URI, fetch timestamp, UTF-8 byte count and SHA-256 digest. The manifest therefore allows an analyst to trace each processed observation set back to the retrieved source payload.
