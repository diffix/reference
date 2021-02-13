# Operation of LCF

This document describes the operation of low count filtering.

## Design

### Configuration parameters

LCF is defined by three parameters:

1. `lower`: The absolute lower bound of the noisy threshold
2. `mean`: The mean value of the normal distribution used to set the noisy threshold
3. `sd`: The standard deviation of the normal distribution

I suggest that `lower` always be a real number like 1.5, not an integer. This way we avoid the question of whether it is 'less than', 'greater than', 'less than or equal', etc. I for one can never remember which it is...

It must not be possible to configure a value of `lower` such that a bucket with one distinct AID is not suppressed.

### Algorithm

The input to the algorithm is the set of AIDs, for each AID type that is active for the query, that comprise the bucket. As discussed in https://github.com/diffix/extension-prototype/issues/21, AIDs are counted even if they contribute nothing to the corresponding aggregate (i.e. `NULL` values).

When there are multiple active AIDs for the queries, then one of them is selected as the `working_aid` with respect to this algorithm. Once selected, only the `working_aid` is used in the algorithm. The other AIDs are ignored.

The `working_aid` is selected as the AID with the smallest number of distinct AIDs. If there are multiple such AIDs, then the `working_aid` is selected as the one with the smallest seed.

The `seed` for an AID is some function of the set of distinct AIDs (i.e. the XOR of the hashes of the AIDs or something). (Note we should only work with hashes of the AIDs, where the hash is salted with the system secret.)

Once we have the `working_aid`, then from this we produce two values:

1. `seed`: The seed from the `working_aid`.
2. `num_distinct`: The number of distinct AIDs of the `working_aid`.

The algorithm then works like this:

1. Generate a normal sticky noise sample `threshold` using `mean` and `sd`. The PRNG is seeded by `seed`.
2. If `threshold` is less than `lower`, set `threshold = lower`.
3. Set a value `upper = mean + (mean - lower)`.  If `threshold` is greater than `upper`, set `threshold = upper`.
4. If `num_distinct` is less than `threshold`, then suppress. Otherwise don't suppress.

## Rationale

The setting of the LCF config parameters represents a trade-off between utility and privacy.

To get good privacy characteristics, the LCF parameters should be set such that the noise sample `threshold` is rarely less than `lower`.

To see how to set these, the program lcfParams.py generates the following three tables.

