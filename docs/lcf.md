# Operation of LCF

This document describes the operation of low count filtering.

## Design

### Configuration parameters

LCF is defined by three parameters:

1. `always_suppress_upper_bound`: The upper bound of distinct AIDs at which buckets will always be suppressed
2. `mean`: The mean value of the normal distribution used to set the noisy threshold
3. `sd`: The standard deviation of the normal distribution

It must not be possible to configure a value of `always_suppress_upper_bound` such that a bucket with one distinct AID is not suppressed.

### Algorithm

The input to the algorithm is the set of AIDs, for each AID type that is active for the query, that comprise the bucket. As discussed in https://github.com/diffix/extension-prototype/issues/21, AIDs are counted even if they contribute nothing to the corresponding aggregate (i.e. `NULL` values).

When there are multiple active AIDs for the queries, then one of them is selected as the `working_aid` with respect to this algorithm. Once selected, only the `working_aid` is used in the algorithm. The other AIDs are ignored.

The `working_aid` is selected as the AID with the smallest number of distinct AIDs. If there are multiple such AIDs, then the `working_aid` is selected as the one with the smallest seed.

The `seed` for an AID is some function of the set of distinct AIDs (i.e. the XOR of the hashes of the AIDs or something). (Note we should only work with hashes of the AIDs, where the hash is salted with the system secret.)

> Note: The reason for a `working_aid`, rather than for instance jumbling up all the AIDs into one seed, is to prevent attacks where one AID is coarser than the other (say `aid_user` and `aid_branch`), and the coarser AID (i.e. `aid_branch`) is attacked by generating different sets of `aid_user` while keeping `aid_branch` consistent, thus eliminating the effect of the seed through averaging. Not sure this is a legitimate attack, but in any event selecting a `working_aid` takes care of it with no negative effect so far as I can see.

Once we have the `working_aid`, then from this we produce two values:

1. `seed`: The seed from the `working_aid`.
2. `num_distinct`: The number of distinct AIDs of the `working_aid`.

The algorithm then works like this:

1. Generate a normal sticky noise sample `threshold` using `mean` and `sd`. The PRNG is seeded by `seed`.
2. If `threshold` is less than `always_suppress_upper_bound`, set `threshold = always_suppress_upper_bound`.
3. Set a value `upper = mean + (mean - always_suppress_upper_bound)`.  If `threshold` is greater than `upper`, set `threshold = upper`.
4. If `num_distinct` is less than or equal to `threshold`, then suppress. Otherwise don't suppress.

## Rationale and Config Settings

The setting of the LCF config parameters represents a trade-off between utility and privacy.

To get a sense of how to set the LCF config parameters, the program lcfParams.py generates a bunch of data using different LCF parameters, as follows:

*mean*: Different `mean` values from 3 to 9. Higher `mean` is more private.

*always_suppress_upper_bound*: `always_suppress_upper_bound` values of 1 and 2. Higher `always_suppress_upper_bound` is more private.

*sd*: SDs set as the distance from `mean` to `int(always_suppress_upper_bound)` divided by 2, 3, and 4. Effect of `sd` will be shown in the results.

The main purpose of LCF is to prevent an attacker from simply displaying private data with `SELECT *`. The reason we make the threshold noisy, however, is to avoid situations where an attacker is able to deduce something about a single user depending on whether a bucket was suppressed or reported. If there is a hard threshold, then in cases where an analyst knows that there are N or N+1 AIDs in a bucket (where the hard threshold is N), then the analyst knows with certainty whether the N+1th AID is included based on whether the bucket was reported or suppressed.

Following is an example of how a noisy threshold works with strong privacy. Here, `mean=8` and `sd=1.5`. The following table shows the probability that a bucket with N AIDs will be reported.

| (mean,sd)   |       2 |       3 |       4 |       5 |       6 |       7 |       8 |       9 |      10 |
|:------------|--------:|--------:|--------:|--------:|--------:|--------:|--------:|--------:|--------:|
| (8.0,1.5)   | 3e-05   | 0.00043 | 0.00374 | 0.02296 | 0.09141 | 0.25211 | 0.49983 | 0.7476  | 0.90894 |

With this setting, if we were to set `always_suppress_upper_bound=2`, then the hard threshold would almost never be invoked, roughly once in every 30K buckets. What this means from a practical perspective is that if an analyst knows that a bucket has either 2 or 3 users in it, it would be very rare that the bucket is reported, thus revealing with 100% certainty that there are in fact 3 AIDs in the bucket.

We can see this certainty in the table below. This table represents the case where the attacker knows that there are either N or N+1 AIDs in a given bucket, each with 50% probability. Here `always_suppress_upper_bound=2`. If there are 2 or 3 AIDs (N=2), then if the bucket is reported, the attacker knows with 100% certainty that there are 3 AIDs in the bucket. However, this happens once in every 4500 or so buckets. As N grows, the likelihood of a bucket being reported grows, but the confidence in the guess shrinks. (The 1/1000000++ notation simply means that the bucket was never reported in 1000000 trials, so the true probability is unknown.)

| (mean,sd)   | N   | Prob N AIDs (sup)   | Prob N+1 AIDs (rep)   | Prob reported   |
|:------------|:----|:--------------------|:----------------------|:----------------|
| (8.0,1.5)   | 1   | 0.500               | 1                     | 1/1000000++     |
| (8.0,1.5)   | 2   | 0.500               | 1.00                  | 1/4761          |
| (8.0,1.5)   | 3   | 0.501               | 0.90                  | 1/464           |
| (8.0,1.5)   | 4   | 0.505               | 0.86                  | 1/74            |
| (8.0,1.5)   | 5   | 0.518               | 0.80                  | 1/17            |
| (8.0,1.5)   | 6   | 0.548               | 0.74                  | 1/5             |
| (8.0,1.5)   | 7   | 0.599               | 0.66                  | 1/2             |

So in my mind, `mean=8`, `sd=1.5`, and `always_suppress_upper_bound=2` represents strong LCF. On the other hand, there is lots of suppression in this case. Furthermore, we can ask if it is really necessary to have such strong LCF. There are two mitigating circumstances in particular. First, it is relatively rare that an attacker knows that there is only N or N+1 AIDs in a given bucket, and more rare as N increases. (Why would an attacker happen to know about all but one AID in a bucket? It could happen, but is kindof strange.)

Second, if a column has a lot of buckets with only 2 AIDs (say for instance where `always_suppress_upper_bound=1`), then of course there is a danger that many values can simply be read out with `SELECT col, count(*)`. However, one could disable selection for that column. Or the column itself could be declared an AID (for instance say the column is `account_number` for a bank with many joint accounts). Therefore in many cases a less private setting may be perfectly adequate.

Following is the data for (3.5,0.6) and (4.0,0.8) (`always_suppress_upper_bound=1`).

Again we see that very few buckets would be rejected because of the hard lower bound.

| (mean,sd)   |       1 |       2 |       3 |       4 |       5 |       6 |       7 |       8 |       9 |
|:------------|--------:|--------:|--------:|--------:|--------:|--------:|--------:|--------:|--------:|
| (3.5,0.6)   | 2e-05   | 0.00633 | 0.20207 | 0.79744 | 0.99379 | 1       | 1       | 1       | 1       |
| (4.0,0.8)   | 9e-05   | 0.00621 | 0.10544 | 0.50042 | 0.8939  | 0.99391 | 1       | 1       | 1       |

From the data below, we see that, if the attacker knows that there are either 1 or 2 AIDs in a bucket, then roughly 1 in 300 buckets will be reported and reveal that there are 2 AIDs with certainty. Again, it is rare that an attacker has this kind of knowledge, so I don't think this is much of a problem in practice.

| (mean,sd)   | N   | Prob N AIDs (sup)   | Prob N+1 AIDs (rep)   | Prob reported   |
|:------------|:----|:--------------------|:----------------------|:----------------|
| (3.5,0.6)   | 1   | 0.502               | 1.00                  | 1/318           |
| (3.5,0.6)   | 2   | 0.555               | 0.97                  | 1/9             |
|             |     |                     |                       |                 |
| (4.0,0.8)   | 1   | 0.502               | 1.00                  | 1/326           |
| (4.0,0.8)   | 2   | 0.526               | 0.94                  | 1/17            |
| (4.0,0.8)   | 3   | 0.642               | 0.83                  | 1/3             |

This all suggests to me that a good setting for Knox would indeed be `mean=8`, `sd=1.5`, and `always_suppress_upper_bound=2`. This setting could also be used for Publish *in the case where the output is made public* to be very safe. For use with Public by the trusted analyst, or for Cloak, a setting of `mean=4`, `sd=0.8`, and `always_suppress_upper_bound=1` or `mean=3.5`, `sd=0.6`, and `always_suppress_upper_bound=1` should be fine.



## Data from lcfParams.py


Given the count of distinct AIDs in a bucket, what is the probability that the bucket will be reported (not suppressed). In producing these numbers, we set `always_suppress_upper_bound=0` so that we can see how often the lower limit would have been hit. In practice we would never set `always_suppress_upper_bound=0`.
        
| (mean,sd)   |       1 |       2 |       3 |       4 |       5 |       6 |       7 |       8 |       9 |
|:------------|--------:|--------:|--------:|--------:|--------:|--------:|--------:|--------:|--------:|
| (3.0,1.0)   | 0.02264 | 0.15834 | 0.49955 | 0.84173 | 0.9774  | 0.99866 | 1       | 1       | 1       |
| (3.0,0.7)   | 0.0021  | 0.07621 | 0.50003 | 0.92322 | 0.99786 | 0.99999 | 1       | 1       | 1       |
| (3.0,0.5)   | 3e-05   | 0.02257 | 0.50036 | 0.97722 | 0.99996 | 1       | 1       | 1       | 1       |
| (3.0,0.4)   | 0       | 0.00621 | 0.50026 | 0.9938  | 1       | 1       | 1       | 1       | 1       |
| (3.5,1.2)   | 0.01846 | 0.10571 | 0.33836 | 0.66234 | 0.89456 | 0.98136 | 0.99833 | 1       | 1       |
| (3.5,0.8)   | 0.00093 | 0.03018 | 0.26581 | 0.7334  | 0.96949 | 0.99912 | 1       | 1       | 1       |
| (3.5,0.6)   | 1e-05   | 0.00629 | 0.20187 | 0.79789 | 0.99372 | 0.99999 | 1       | 1       | 1       |
| (3.5,0.5)   | 0       | 0.0013  | 0.15882 | 0.84135 | 0.99867 | 1       | 1       | 1       | 1       |
| (4.0,1.5)   | 0.02282 | 0.09166 | 0.25302 | 0.50008 | 0.74815 | 0.90911 | 0.97701 | 0.99613 | 1       |
| (4.0,1.0)   | 0.00135 | 0.02295 | 0.15828 | 0.49981 | 0.84124 | 0.97734 | 0.99867 | 0.99997 | 1       |
| (4.0,0.8)   | 9e-05   | 0.00617 | 0.10512 | 0.4999  | 0.89411 | 0.99374 | 0.99989 | 1       | 1       |
| (4.0,0.6)   | 0       | 0.00044 | 0.04778 | 0.50033 | 0.95212 | 0.99958 | 1       | 1       | 1       |
| (5.0,2.0)   | 0.02296 | 0.06707 | 0.15846 | 0.30837 | 0.50062 | 0.69125 | 0.84086 | 0.93323 | 0.97736 |
| (5.0,1.3)   | 0.00105 | 0.0105  | 0.062   | 0.22048 | 0.49993 | 0.77889 | 0.9383  | 0.98942 | 0.99897 |
| (5.0,1.0)   | 3e-05   | 0.00139 | 0.02302 | 0.15851 | 0.50074 | 0.84093 | 0.97727 | 0.99859 | 0.99997 |
| (5.0,0.8)   | 0       | 7e-05   | 0.0062  | 0.10601 | 0.50033 | 0.89508 | 0.99362 | 0.99989 | 1       |
| (7.0,3.0)   | 0.0228  | 0.0478  | 0.09122 | 0.15904 | 0.25233 | 0.36936 | 0.49902 | 0.63018 | 0.74826 |
| (7.0,2.0)   | 0.00135 | 0.00635 | 0.02283 | 0.06656 | 0.15824 | 0.30828 | 0.49964 | 0.6916  | 0.84083 |
| (7.0,1.5)   | 3e-05   | 0.00044 | 0.00387 | 0.02258 | 0.09084 | 0.25178 | 0.50039 | 0.74728 | 0.90909 |
| (7.0,1.2)   | 0       | 1e-05   | 0.0004  | 0.00628 | 0.04799 | 0.20224 | 0.50047 | 0.79699 | 0.95211 |
| (9.0,4.0)   | 0.02324 | 0.03997 | 0.06649 | 0.10558 | 0.15899 | 0.22662 | 0.30915 | 0.40117 | 0.49987 |
| (9.0,2.7)   | 0.00152 | 0.00483 | 0.0131  | 0.03193 | 0.06928 | 0.13283 | 0.22852 | 0.35577 | 0.49887 |
| (9.0,2.0)   | 3e-05   | 0.00025 | 0.00137 | 0.00613 | 0.02284 | 0.06676 | 0.15885 | 0.30902 | 0.4997  |
| (9.0,1.6)   | 0       | 1e-05   | 9e-05   | 0.00093 | 0.0061  | 0.03048 | 0.10596 | 0.26612 | 0.49974 |

Suppose that an attacker knows that there are either N or N+1 AIDs in a bucket.  Suppose that the probability of either outcome is 50%. The following table shows three things (`always_suppress_upper_bound=1`):
1. The probability that there is in fact N AIDs given that the bucket is suppressed.
2. The probability that there are in fact N+1 AIDs given that the bucket is reported.
3. The likelihood that the bucket is reported.
        
| (mean,sd)   | N   | Prob N AIDs (sup)   | Prob N+1 AIDs (rep)   | Prob reported   |
|:------------|:----|:--------------------|:----------------------|:----------------|
| (3.0,1.0)   | 1   | 0.542               | 1.00                  | 1/12            |
| (3.0,1.0)   | 2   | 0.627               | 0.76                  | 1/3             |
|             |     |                     |                       |                 |
| (3.0,0.7)   | 1   | 0.521               | 1.00                  | 1/26            |
| (3.0,0.7)   | 2   | 0.649               | 0.87                  | 1/3             |
|             |     |                     |                       |                 |
| (3.0,0.5)   | 1   | 0.506               | 1.00                  | 1/87            |
| (3.0,0.5)   | 2   | 0.662               | 0.96                  | 1/3             |
|             |     |                     |                       |                 |
| (3.0,0.4)   | 1   | 0.501               | 1.00                  | 1/317           |
| (3.0,0.4)   | 2   | 0.666               | 0.99                  | 1/3             |
|             |     |                     |                       |                 |
| (3.5,1.2)   | 1   | 0.528               | 1.00                  | 1/18            |
| (3.5,1.2)   | 2   | 0.574               | 0.76                  | 1/4             |
|             |     |                     |                       |                 |
| (3.5,0.8)   | 1   | 0.507               | 1.00                  | 1/66            |
| (3.5,0.8)   | 2   | 0.569               | 0.90                  | 1/6             |
|             |     |                     |                       |                 |
| (3.5,0.6)   | 1   | 0.502               | 1.00                  | 1/315           |
| (3.5,0.6)   | 2   | 0.554               | 0.97                  | 1/9             |
|             |     |                     |                       |                 |
| (3.5,0.5)   | 1   | 0.501               | 1.00                  | 1/1453          |
| (3.5,0.5)   | 2   | 0.544               | 0.99                  | 1/12            |
|             |     |                     |                       |                 |
| (4.0,1.5)   | 1   | 0.524               | 1.00                  | 1/21            |
| (4.0,1.5)   | 2   | 0.548               | 0.74                  | 1/5             |
| (4.0,1.5)   | 3   | 0.599               | 0.66                  | 1/2             |
|             |     |                     |                       |                 |
| (4.0,1.0)   | 1   | 0.507               | 1.00                  | 1/88            |
| (4.0,1.0)   | 2   | 0.537               | 0.87                  | 1/11            |
| (4.0,1.0)   | 3   | 0.627               | 0.76                  | 1/3             |
|             |     |                     |                       |                 |
| (4.0,0.8)   | 1   | 0.502               | 1.00                  | 1/314           |
| (4.0,0.8)   | 2   | 0.526               | 0.94                  | 1/17            |
| (4.0,0.8)   | 3   | 0.641               | 0.83                  | 1/3             |
|             |     |                     |                       |                 |
| (4.0,0.6)   | 1   | 0.501               | 1.00                  | 1/4761          |
| (4.0,0.6)   | 2   | 0.513               | 0.99                  | 1/41            |
| (4.0,0.6)   | 3   | 0.655               | 0.91                  | 1/3             |
|             |     |                     |                       |                 |
| (5.0,2.0)   | 1   | 0.517               | 1.00                  | 1/30            |
| (5.0,2.0)   | 2   | 0.526               | 0.70                  | 1/8             |
| (5.0,2.0)   | 3   | 0.549               | 0.66                  | 1/4             |
| (5.0,2.0)   | 4   | 0.581               | 0.62                  | 1/2             |
|             |     |                     |                       |                 |
| (5.0,1.3)   | 1   | 0.503               | 1.00                  | 1/192           |
| (5.0,1.3)   | 2   | 0.514               | 0.85                  | 1/27            |
| (5.0,1.3)   | 3   | 0.546               | 0.78                  | 1/7             |
| (5.0,1.3)   | 4   | 0.610               | 0.69                  | 1/2             |
|             |     |                     |                       |                 |
| (5.0,1.0)   | 1   | 0.501               | 1.00                  | 1/1618          |
| (5.0,1.0)   | 2   | 0.506               | 0.94                  | 1/82            |
| (5.0,1.0)   | 3   | 0.538               | 0.87                  | 1/11            |
| (5.0,1.0)   | 4   | 0.628               | 0.76                  | 1/3             |
|             |     |                     |                       |                 |
| (5.0,0.8)   | 1   | 0.501               | 1.00                  | 1/28571         |
| (5.0,0.8)   | 2   | 0.502               | 0.99                  | 1/317           |
| (5.0,0.8)   | 3   | 0.526               | 0.94                  | 1/17            |
| (5.0,0.8)   | 4   | 0.641               | 0.83                  | 1/3             |
|             |     |                     |                       |                 |
| (7.0,3.0)   | 1   | 0.513               | 1.00                  | 1/41            |
| (7.0,3.0)   | 2   | 0.513               | 0.65                  | 1/14            |
| (7.0,3.0)   | 3   | 0.520               | 0.63                  | 1/8             |
| (7.0,3.0)   | 4   | 0.529               | 0.62                  | 1/4             |
| (7.0,3.0)   | 5   | 0.543               | 0.59                  | 1/3             |
| (7.0,3.0)   | 6   | 0.557               | 0.57                  | 1/2             |
|             |     |                     |                       |                 |
| (7.0,2.0)   | 1   | 0.502               | 1.00                  | 1/320           |
| (7.0,2.0)   | 2   | 0.504               | 0.78                  | 1/68            |
| (7.0,2.0)   | 3   | 0.512               | 0.75                  | 1/22            |
| (7.0,2.0)   | 4   | 0.526               | 0.71                  | 1/8             |
| (7.0,2.0)   | 5   | 0.549               | 0.66                  | 1/4             |
| (7.0,2.0)   | 6   | 0.582               | 0.62                  | 1/2             |
|             |     |                     |                       |                 |
| (7.0,1.5)   | 1   | 0.500               | 1.00                  | 1/4464          |
| (7.0,1.5)   | 2   | 0.502               | 0.90                  | 1/476           |
| (7.0,1.5)   | 3   | 0.505               | 0.86                  | 1/74            |
| (7.0,1.5)   | 4   | 0.519               | 0.80                  | 1/17            |
| (7.0,1.5)   | 5   | 0.548               | 0.74                  | 1/5             |
| (7.0,1.5)   | 6   | 0.600               | 0.66                  | 1/2             |
|             |     |                     |                       |                 |
| (7.0,1.2)   | 1   | 0.500               | 1.00                  | 1/142857        |
| (7.0,1.2)   | 2   | 0.500               | 0.99                  | 1/4784          |
| (7.0,1.2)   | 3   | 0.501               | 0.93                  | 1/302           |
| (7.0,1.2)   | 4   | 0.511               | 0.89                  | 1/36            |
| (7.0,1.2)   | 5   | 0.545               | 0.81                  | 1/7             |
| (7.0,1.2)   | 6   | 0.614               | 0.71                  | 1/2             |
|             |     |                     |                       |                 |
| (9.0,4.0)   | 1   | 0.511               | 1.00                  | 1/49            |
| (9.0,4.0)   | 2   | 0.506               | 0.62                  | 1/18            |
| (9.0,4.0)   | 3   | 0.512               | 0.61                  | 1/11            |
| (9.0,4.0)   | 4   | 0.515               | 0.60                  | 1/7             |
| (9.0,4.0)   | 5   | 0.521               | 0.59                  | 1/5             |
| (9.0,4.0)   | 6   | 0.527               | 0.58                  | 1/3             |
| (9.0,4.0)   | 7   | 0.536               | 0.57                  | 1/2             |
| (9.0,4.0)   | 8   | 0.544               | 0.56                  | 1/2             |
|             |     |                     |                       |                 |
| (9.0,2.7)   | 1   | 0.502               | 1.00                  | 1/431           |
| (9.0,2.7)   | 2   | 0.502               | 0.74                  | 1/113           |
| (9.0,2.7)   | 3   | 0.506               | 0.72                  | 1/44            |
| (9.0,2.7)   | 4   | 0.509               | 0.68                  | 1/19            |
| (9.0,2.7)   | 5   | 0.518               | 0.66                  | 1/9             |
| (9.0,2.7)   | 6   | 0.529               | 0.63                  | 1/5             |
| (9.0,2.7)   | 7   | 0.544               | 0.61                  | 1/3             |
| (9.0,2.7)   | 8   | 0.563               | 0.58                  | 1/2             |
|             |     |                     |                       |                 |
| (9.0,2.0)   | 1   | 0.500               | 1.00                  | 1/9345          |
| (9.0,2.0)   | 2   | 0.500               | 0.85                  | 1/1264          |
| (9.0,2.0)   | 3   | 0.501               | 0.82                  | 1/257           |
| (9.0,2.0)   | 4   | 0.504               | 0.79                  | 1/68            |
| (9.0,2.0)   | 5   | 0.512               | 0.75                  | 1/22            |
| (9.0,2.0)   | 6   | 0.527               | 0.71                  | 1/8             |
| (9.0,2.0)   | 7   | 0.548               | 0.66                  | 1/4             |
| (9.0,2.0)   | 8   | 0.580               | 0.62                  | 1/2             |
|             |     |                     |                       |                 |
| (9.0,1.6)   | 1   | 0.500               | 1.00                  | 1/1000000       |
| (9.0,1.6)   | 2   | 0.500               | 0.90                  | 1/19607         |
| (9.0,1.6)   | 3   | 0.500               | 0.92                  | 1/1937          |
| (9.0,1.6)   | 4   | 0.502               | 0.87                  | 1/289           |
| (9.0,1.6)   | 5   | 0.505               | 0.83                  | 1/54            |
| (9.0,1.6)   | 6   | 0.520               | 0.78                  | 1/14            |
| (9.0,1.6)   | 7   | 0.549               | 0.72                  | 1/5             |
| (9.0,1.6)   | 8   | 0.595               | 0.65                  | 1/2             |
|             |     |                     |                       |                 |

Given the count of distinct AIDs in a bucket, what is the probability that the bucket will be reported (not suppressed). In producing these numbers, we set `always_suppress_upper_bound=0` so that we can see how often the lower limit would have been hit. In practice we would never set `always_suppress_upper_bound=0`.
        
| (mean,sd)   |       2 |       3 |       4 |       5 |       6 |       7 |       8 |       9 |      10 |
|:------------|--------:|--------:|--------:|--------:|--------:|--------:|--------:|--------:|--------:|
| (4.0,1.0)   | 0.02265 | 0.15877 | 0.5006  | 0.84182 | 0.977   | 0.99869 | 0.99997 | 1       | 1       |
| (4.0,0.7)   | 0.00206 | 0.07632 | 0.50008 | 0.92337 | 0.99788 | 0.99999 | 1       | 1       | 1       |
| (4.0,0.5)   | 4e-05   | 0.02268 | 0.49976 | 0.9774  | 0.99998 | 1       | 1       | 1       | 1       |
| (4.0,0.4)   | 0       | 0.00637 | 0.50008 | 0.9938  | 1       | 1       | 1       | 1       | 1       |
| (4.5,1.2)   | 0.01871 | 0.10546 | 0.3377  | 0.66145 | 0.89414 | 0.98147 | 0.99823 | 0.99991 | 1       |
| (4.5,0.8)   | 0.00093 | 0.03025 | 0.26618 | 0.73286 | 0.96948 | 0.99907 | 0.99999 | 1       | 1       |
| (4.5,0.6)   | 2e-05   | 0.00616 | 0.20184 | 0.79824 | 0.99377 | 0.99999 | 1       | 1       | 1       |
| (4.5,0.5)   | 0       | 0.00136 | 0.15868 | 0.84157 | 0.99868 | 1       | 1       | 1       | 1       |
| (5.0,1.5)   | 0.02286 | 0.09093 | 0.2524  | 0.50013 | 0.74731 | 0.9083  | 0.97699 | 0.99606 | 0.99959 |
| (5.0,1.0)   | 0.00135 | 0.02271 | 0.15806 | 0.49997 | 0.84148 | 0.97724 | 0.99868 | 0.99997 | 1       |
| (5.0,0.8)   | 8e-05   | 0.00629 | 0.10622 | 0.50041 | 0.89353 | 0.99386 | 0.99989 | 1       | 1       |
| (5.0,0.6)   | 0       | 0.00043 | 0.0476  | 0.50031 | 0.95216 | 0.99955 | 1       | 1       | 1       |
| (6.0,2.0)   | 0.02242 | 0.06662 | 0.15873 | 0.30865 | 0.49949 | 0.69133 | 0.84114 | 0.93351 | 0.97708 |
| (6.0,1.3)   | 0.00105 | 0.01052 | 0.06235 | 0.22034 | 0.50081 | 0.77973 | 0.93847 | 0.98942 | 0.99898 |
| (6.0,1.0)   | 3e-05   | 0.00137 | 0.02255 | 0.15905 | 0.50067 | 0.84105 | 0.97731 | 0.99868 | 0.99996 |
| (6.0,0.8)   | 0       | 9e-05   | 0.0061  | 0.105   | 0.4997  | 0.89487 | 0.9938  | 0.99991 | 1       |
| (8.0,3.0)   | 0.02283 | 0.04763 | 0.09142 | 0.15773 | 0.25307 | 0.36936 | 0.50103 | 0.63094 | 0.74765 |
| (8.0,2.0)   | 0.00136 | 0.00624 | 0.02271 | 0.06652 | 0.15883 | 0.30857 | 0.50021 | 0.69107 | 0.84134 |
| (8.0,1.5)   | 3e-05   | 0.00043 | 0.00374 | 0.02296 | 0.09141 | 0.25211 | 0.49983 | 0.7476  | 0.90894 |
| (8.0,1.2)   | 0       | 1e-05   | 0.00047 | 0.00634 | 0.04798 | 0.20235 | 0.49953 | 0.79807 | 0.95221 |
| (10.0,4.0)  | 0.02257 | 0.03978 | 0.06692 | 0.10543 | 0.15896 | 0.22618 | 0.30912 | 0.4018  | 0.50117 |
| (10.0,2.7)  | 0.00154 | 0.00485 | 0.01304 | 0.03207 | 0.06921 | 0.13336 | 0.22945 | 0.35506 | 0.49978 |
| (10.0,2.0)  | 2e-05   | 0.00024 | 0.00136 | 0.00628 | 0.02278 | 0.06677 | 0.15845 | 0.30867 | 0.50043 |
| (10.0,1.6)  | 0       | 1e-05   | 8e-05   | 0.00087 | 0.00638 | 0.03027 | 0.10594 | 0.26545 | 0.50001 |

Suppose that an attacker knows that there are either N or N+1 AIDs in a bucket.  Suppose that the probability of either outcome is 50%. The following table shows three things (`always_suppress_upper_bound=2`):
1. The probability that there is in fact N AIDs given that the bucket is suppressed.
2. The probability that there are in fact N+1 AIDs given that the bucket is reported.
3. The likelihood that the bucket is reported.
        
| (mean,sd)   | N   | Prob N AIDs (sup)   | Prob N+1 AIDs (rep)   | Prob reported   |
|:------------|:----|:--------------------|:----------------------|:----------------|
| (4.0,1.0)   | 1   | 0.500               | 1                     | 1/1000000++     |
| (4.0,1.0)   | 2   | 0.543               | 1.00                  | 1/12            |
| (4.0,1.0)   | 3   | 0.628               | 0.76                  | 1/3             |
|             |     |                     |                       |                 |
| (4.0,0.7)   | 1   | 0.501               | 1                     | 1/1000000++     |
| (4.0,0.7)   | 2   | 0.520               | 1.00                  | 1/26            |
| (4.0,0.7)   | 3   | 0.648               | 0.87                  | 1/3             |
|             |     |                     |                       |                 |
| (4.0,0.5)   | 1   | 0.501               | 1                     | 1/1000000++     |
| (4.0,0.5)   | 2   | 0.506               | 1.00                  | 1/87            |
| (4.0,0.5)   | 3   | 0.662               | 0.96                  | 1/3             |
|             |     |                     |                       |                 |
| (4.0,0.4)   | 1   | 0.500               | 1                     | 1/1000000++     |
| (4.0,0.4)   | 2   | 0.501               | 1.00                  | 1/318           |
| (4.0,0.4)   | 3   | 0.665               | 0.99                  | 1/3             |
|             |     |                     |                       |                 |
| (4.5,1.2)   | 1   | 0.499               | 1                     | 1/1000000++     |
| (4.5,1.2)   | 2   | 0.529               | 1.00                  | 1/18            |
| (4.5,1.2)   | 3   | 0.575               | 0.76                  | 1/4             |
|             |     |                     |                       |                 |
| (4.5,0.8)   | 1   | 0.500               | 1                     | 1/1000000++     |
| (4.5,0.8)   | 2   | 0.508               | 1.00                  | 1/66            |
| (4.5,0.8)   | 3   | 0.568               | 0.90                  | 1/6             |
|             |     |                     |                       |                 |
| (4.5,0.6)   | 1   | 0.500               | 1                     | 1/1000000++     |
| (4.5,0.6)   | 2   | 0.501               | 1.00                  | 1/324           |
| (4.5,0.6)   | 3   | 0.554               | 0.97                  | 1/9             |
|             |     |                     |                       |                 |
| (4.5,0.5)   | 1   | 0.499               | 1                     | 1/1000000++     |
| (4.5,0.5)   | 2   | 0.500               | 1.00                  | 1/1515          |
| (4.5,0.5)   | 3   | 0.542               | 0.99                  | 1/12            |
|             |     |                     |                       |                 |
| (5.0,1.5)   | 1   | 0.500               | 1                     | 1/1000000++     |
| (5.0,1.5)   | 2   | 0.524               | 1.00                  | 1/21            |
| (5.0,1.5)   | 3   | 0.548               | 0.73                  | 1/5             |
| (5.0,1.5)   | 4   | 0.600               | 0.66                  | 1/2             |
|             |     |                     |                       |                 |
| (5.0,1.0)   | 1   | 0.501               | 1                     | 1/1000000++     |
| (5.0,1.0)   | 2   | 0.505               | 1.00                  | 1/87            |
| (5.0,1.0)   | 3   | 0.537               | 0.87                  | 1/11            |
| (5.0,1.0)   | 4   | 0.626               | 0.76                  | 1/3             |
|             |     |                     |                       |                 |
| (5.0,0.8)   | 1   | 0.500               | 1                     | 1/1000000++     |
| (5.0,0.8)   | 2   | 0.502               | 1.00                  | 1/328           |
| (5.0,0.8)   | 3   | 0.526               | 0.94                  | 1/17            |
| (5.0,0.8)   | 4   | 0.642               | 0.83                  | 1/3             |
|             |     |                     |                       |                 |
| (5.0,0.6)   | 1   | 0.500               | 1                     | 1/1000000++     |
| (5.0,0.6)   | 2   | 0.500               | 1.00                  | 1/5291          |
| (5.0,0.6)   | 3   | 0.512               | 0.99                  | 1/41            |
| (5.0,0.6)   | 4   | 0.656               | 0.91                  | 1/3             |
|             |     |                     |                       |                 |
| (6.0,2.0)   | 1   | 0.501               | 1                     | 1/1000000++     |
| (6.0,2.0)   | 2   | 0.517               | 1.00                  | 1/30            |
| (6.0,2.0)   | 3   | 0.525               | 0.70                  | 1/8             |
| (6.0,2.0)   | 4   | 0.550               | 0.66                  | 1/4             |
| (6.0,2.0)   | 5   | 0.581               | 0.62                  | 1/2             |
|             |     |                     |                       |                 |
| (6.0,1.3)   | 1   | 0.500               | 1                     | 1/1000000++     |
| (6.0,1.3)   | 2   | 0.503               | 1.00                  | 1/192           |
| (6.0,1.3)   | 3   | 0.513               | 0.86                  | 1/27            |
| (6.0,1.3)   | 4   | 0.546               | 0.78                  | 1/7             |
| (6.0,1.3)   | 5   | 0.608               | 0.69                  | 1/2             |
|             |     |                     |                       |                 |
| (6.0,1.0)   | 1   | 0.500               | 1                     | 1/1000000++     |
| (6.0,1.0)   | 2   | 0.500               | 1.00                  | 1/1428          |
| (6.0,1.0)   | 3   | 0.505               | 0.94                  | 1/82            |
| (6.0,1.0)   | 4   | 0.537               | 0.87                  | 1/11            |
| (6.0,1.0)   | 5   | 0.627               | 0.76                  | 1/3             |
|             |     |                     |                       |                 |
| (6.0,0.8)   | 1   | 0.499               | 1                     | 1/1000000++     |
| (6.0,0.8)   | 2   | 0.500               | 1.00                  | 1/26315         |
| (6.0,0.8)   | 3   | 0.501               | 0.99                  | 1/318           |
| (6.0,0.8)   | 4   | 0.528               | 0.94                  | 1/17            |
| (6.0,0.8)   | 5   | 0.641               | 0.82                  | 1/3             |
|             |     |                     |                       |                 |
| (8.0,3.0)   | 1   | 0.500               | 1                     | 1/1000000++     |
| (8.0,3.0)   | 2   | 0.512               | 1.00                  | 1/41            |
| (8.0,3.0)   | 3   | 0.512               | 0.65                  | 1/14            |
| (8.0,3.0)   | 4   | 0.519               | 0.64                  | 1/8             |
| (8.0,3.0)   | 5   | 0.529               | 0.61                  | 1/4             |
| (8.0,3.0)   | 6   | 0.542               | 0.59                  | 1/3             |
| (8.0,3.0)   | 7   | 0.558               | 0.58                  | 1/2             |
|             |     |                     |                       |                 |
| (8.0,2.0)   | 1   | 0.500               | 1                     | 1/1000000++     |
| (8.0,2.0)   | 2   | 0.502               | 1.00                  | 1/329           |
| (8.0,2.0)   | 3   | 0.504               | 0.79                  | 1/69            |
| (8.0,2.0)   | 4   | 0.511               | 0.75                  | 1/22            |
| (8.0,2.0)   | 5   | 0.526               | 0.70                  | 1/8             |
| (8.0,2.0)   | 6   | 0.548               | 0.66                  | 1/4             |
| (8.0,2.0)   | 7   | 0.579               | 0.62                  | 1/2             |
|             |     |                     |                       |                 |
| (8.0,1.5)   | 1   | 0.500               | 1                     | 1/1000000++     |
| (8.0,1.5)   | 2   | 0.500               | 1.00                  | 1/4761          |
| (8.0,1.5)   | 3   | 0.501               | 0.90                  | 1/464           |
| (8.0,1.5)   | 4   | 0.505               | 0.86                  | 1/74            |
| (8.0,1.5)   | 5   | 0.518               | 0.80                  | 1/17            |
| (8.0,1.5)   | 6   | 0.548               | 0.74                  | 1/5             |
| (8.0,1.5)   | 7   | 0.599               | 0.66                  | 1/2             |
|             |     |                     |                       |                 |
| (8.0,1.2)   | 1   | 0.500               | 1                     | 1/1000000++     |
| (8.0,1.2)   | 2   | 0.500               | 1.00                  | 1/125000        |
| (8.0,1.2)   | 3   | 0.500               | 0.94                  | 1/4347          |
| (8.0,1.2)   | 4   | 0.501               | 0.94                  | 1/299           |
| (8.0,1.2)   | 5   | 0.511               | 0.89                  | 1/37            |
| (8.0,1.2)   | 6   | 0.544               | 0.81                  | 1/7             |
| (8.0,1.2)   | 7   | 0.616               | 0.71                  | 1/2             |
|             |     |                     |                       |                 |
| (10.0,4.0)  | 1   | 0.500               | 1                     | 1/1000000++     |
| (10.0,4.0)  | 2   | 0.510               | 1.00                  | 1/50            |
| (10.0,4.0)  | 3   | 0.508               | 0.63                  | 1/18            |
| (10.0,4.0)  | 4   | 0.511               | 0.61                  | 1/11            |
| (10.0,4.0)  | 5   | 0.515               | 0.60                  | 1/7             |
| (10.0,4.0)  | 6   | 0.521               | 0.59                  | 1/5             |
| (10.0,4.0)  | 7   | 0.528               | 0.58                  | 1/3             |
| (10.0,4.0)  | 8   | 0.536               | 0.57                  | 1/2             |
| (10.0,4.0)  | 9   | 0.546               | 0.56                  | 1/2             |
|             |     |                     |                       |                 |
| (10.0,2.7)  | 1   | 0.500               | 1                     | 1/1000000++     |
| (10.0,2.7)  | 2   | 0.501               | 1.00                  | 1/408           |
| (10.0,2.7)  | 3   | 0.502               | 0.73                  | 1/109           |
| (10.0,2.7)  | 4   | 0.505               | 0.71                  | 1/44            |
| (10.0,2.7)  | 5   | 0.509               | 0.68                  | 1/19            |
| (10.0,2.7)  | 6   | 0.519               | 0.66                  | 1/9             |
| (10.0,2.7)  | 7   | 0.530               | 0.63                  | 1/5             |
| (10.0,2.7)  | 8   | 0.545               | 0.61                  | 1/3             |
| (10.0,2.7)  | 9   | 0.562               | 0.58                  | 1/2             |
|             |     |                     |                       |                 |
| (10.0,2.0)  | 1   | 0.500               | 1                     | 1/1000000++     |
| (10.0,2.0)  | 2   | 0.500               | 1.00                  | 1/9433          |
| (10.0,2.0)  | 3   | 0.500               | 0.84                  | 1/1340          |
| (10.0,2.0)  | 4   | 0.502               | 0.82                  | 1/265           |
| (10.0,2.0)  | 5   | 0.504               | 0.79                  | 1/69            |
| (10.0,2.0)  | 6   | 0.511               | 0.75                  | 1/22            |
| (10.0,2.0)  | 7   | 0.525               | 0.70                  | 1/8             |
| (10.0,2.0)  | 8   | 0.548               | 0.66                  | 1/4             |
| (10.0,2.0)  | 9   | 0.581               | 0.62                  | 1/2             |
|             |     |                     |                       |                 |
| (10.0,1.6)  | 1   | 0.500               | 1                     | 1/1000000++     |
| (10.0,1.6)  | 2   | 0.499               | 1.00                  | 1/200000        |
| (10.0,1.6)  | 3   | 0.500               | 0.90                  | 1/32258         |
| (10.0,1.6)  | 4   | 0.501               | 0.90                  | 1/2100          |
| (10.0,1.6)  | 5   | 0.500               | 0.88                  | 1/275           |
| (10.0,1.6)  | 6   | 0.506               | 0.83                  | 1/54            |
| (10.0,1.6)  | 7   | 0.521               | 0.78                  | 1/14            |
| (10.0,1.6)  | 8   | 0.548               | 0.71                  | 1/5             |
| (10.0,1.6)  | 9   | 0.594               | 0.65                  | 1/2             |
|             |     |                     |                       |                 |
