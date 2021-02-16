# Operation of LCF

This document describes the operation of low count filtering. The design departs a little from what we have been doing in the past. The original concept of LCF (years ago) was what I would call a *wide-spread* approach, where the hard lower-bound is pretty far away from the mean. This approach leads to a lot of suppression because the mean ends up being pretty high.

What I'm proposing now is a *thin-spread* approach, where the lower-bound is higher, and the mean is just above the lower-bound. This is probably not quite as secure as a wide-spread approach, but it is still very secure and much simpler to explain and understand. The main concern is cases where somehow the attacker knows that a given bucket has exactly K-1 or K AIDs, where K is the minimum allowed number of AIDs. In this case, whether or not a bucket is reported reveals the exact number of AIDs. In practice, if K is relatively high, say 4 or 5 or more, then the chances than the analyst knows this information is very small.

## Design

### Configuration parameters

LCF is defined by one parameter:

1. `minimum_allowed_aids`: The minimum number of distinct AIDs that can be in a reported bucket. Buckets with any number of distinct AIDs lower than this will be suppressed.

This parameter is defined separately for each AID type.

The minimum possible `minimum_allowed_aids` must be 2.

When the AID identifies a human individual, then the following values are recommended:

Situation | `minimum_allowed_aids` | Comments
---       | ---                    | ---
Very high trust | 2-3 | Analyst that has access to raw data
Moderate trust | 4-5 | Analyst in-company or under contract
Low trust | >7 | Public (or data released to public)

Note that `minimum_allowed_aids = 3` is roughly equivalent to the current Diffix Dogwood default setting.

When the AID identifies something else, then probably 2 or 3 is fine in all cases.

## Suppression Decision Algorithm

The input to the suppression decision algorithm is the set of AIDs, for each AID type that is active for the query, that comprise the bucket. As discussed in https://github.com/diffix/extension-prototype/issues/21, AIDs are counted even if they contribute nothing to the corresponding aggregate (i.e. `NULL` values).

The suppression decision is run separately for each active AID type. If any one AID type results in a decision to suppress, then we suppress.

The `seed` for an AID is some function of the set of distinct AIDs (i.e. the XOR of the hashes of the AIDs or something). (Note we should only work with hashes of the AIDs, where the hash is salted with the system secret.)

The per-AID decision then goes like this:

1. With a PRNG seeded by `seed`, select a threshold uniformly among three values, `K`, `K+1`, and `K+2`, where `K=minimum_allowed_aids`.
2. If `num_distinct` is less than `threshold`, then suppress.

> Note that since we no longer have a permanent lower bound value, we'll have to take `minimum_allowed_aids` into account when deciding what aggregate value to report for any given aggregate. The reason we need this is because if the reported aggregate value is well below that which is possible based on `minimum_allowed_aids`, then the analyst would know with high confidence that there are exactly `minimum_allowed_aids` AIDs in the bucket.  How to determine the lower bound reported value is specified somewhere else.
