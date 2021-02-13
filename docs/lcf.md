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

## Rationale and Config Settings

The setting of the LCF config parameters represents a trade-off between utility and privacy.

To get a sense of how to set the LCF config parameters, the program lcfParams.py generates a bunch of data using different LCF parameters, as follows:

*mean*: Different `mean` values from 3 to 9. Higher `mean` is more private.

*lower*: `lower` values of 1.5 and 2.5. Higher `lower` is more private.

*sd*: SDs set as the distance from `mean` to `int(lower)` divided by 2, 3, and 4. Effect of `sd` will be shown in the results.

The main purpose of LCF is to prevent an attacker from simply displaying private data with `SELECT *`. The reason we make the threshold noisy, however, is to avoid situations where an attacker is able to deduce something about a single user depending on whether a bucket was suppressed or reported. If there is a hard threshold, then in cases where an analyst knows that there are N or N+1 AIDs in a bucket (where the hard threshold is N), then the analyst knows with certainty whether the N+1th AID is included based on whether the bucket was reported or suppressed.

Following is an example of how a noisy threshold works with strong privacy. Here, `mean=8` and `sd=1.5`. The following table shows the probability that a bucket with N AIDs will be reported.

| (mean,sd)   |       2 |       3 |       4 |       5 |       6 |       7 |       8 |       9 |      10 |
|:------------|--------:|--------:|--------:|--------:|--------:|--------:|--------:|--------:|--------:|
| (8.0,1.5)   | 3e-05   | 0.00044 | 0.00383 | 0.02286 | 0.0912  | 0.25181 | 0.49994 | 0.7468  | 0.90866 |

With this setting, if we were to set `lower=2.5`, then the hard threshold would almost never be invoked, roughly once in every 30K buckets. What this means from a practical perspective is that if an analyst knows that a bucket has either 2 or 3 users in it, it would be very rare that the bucket is reported, thus revealing with 100% certainty that there are in fact 3 AIDs in the bucket.

We can see this in the table below. This represents the case where the attacker knows that there are either N or N+1 AIDs in a given bucket, each with 50% probability. Here `lower=2.5`. If there are 2 or 3 AIDs (N=2), then if the bucket is reported, the attacker knows with 100% certainty that there are 3 AIDs in the bucket. However, this happens once in every 4500 or so buckets. As N grows, the likelihood of a bucket being reported grows, but the confidence in the guess shrinks.
| (mean,sd)   | N   | Prob N AIDs (sup)   | Prob N+1 AIDs (rep)   | Prob reported   |
|:------------|:----|:--------------------|:----------------------|:----------------|
| (8.0,1.5)   | 1   | 0.499               | 1                     | 1000000++       |
| (8.0,1.5)   | 2   | 0.500               | 1.00                  | 1/4504          |
| (8.0,1.5)   | 3   | 0.500               | 0.90                  | 1/470           |
| (8.0,1.5)   | 4   | 0.505               | 0.85                  | 1/75            |
| (8.0,1.5)   | 5   | 0.518               | 0.80                  | 1/17            |
| (8.0,1.5)   | 6   | 0.549               | 0.73                  | 1/5             |
| (8.0,1.5)   | 7   | 0.600               | 0.66                  | 1/2             |

So in my mind, `mean=8`, `sd=1.5`, and `lower=2.5` represents strong LCF. On the other hand, there is lots of suppression in this case. Furthermore, we can ask if it is really necessary to have such strong LCF. There are two mitigating circumstances in particular. First, it is relatively rare that an attacker knows that there is only N or N+1 AIDs in a given bucket, and more rare as N increases. (Why would an attacker happen to know about all but one AID in a bucket? It could happen, but kindof strange.)

Second, if a column has a lot of buckets with only 2 AIDs (say for instance where `lower=1.5`), then of course there is a danger that many values can simply be read out with `SELECT col, count(*)`. However, one could disable selection for that column. Or the column itself could be declared an AID (for instance say the column is `account_number` for a bank with many joint accounts). Therefore in many cases a less private setting may be perfectly adequate.

Following is the data for (3.5,0.6) and (4.0,0.8) (`lower=1.5`).

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

This all suggests to me that a good setting for Knox would indeed be `mean=8`, `sd=1.5`, and `lower=2.5`. This setting could also be used for Publish *in the case where the output is made public* to be very safe. For use with Public by the trusted analyst, or for Cloak, a setting of `mean=4`, `sd=0.8`, and `lower=1.5` or `mean=3.5`, `sd=0.6`, and `lower=1.5` should be fine.



## Data from lcfParams.py


Given the count of distinct AIDs in a bucket, what is the probability that the bucket will be reported (not suppressed) (`lower=0`):
        
| (mean,sd)   |       1 |       2 |       3 |       4 |       5 |       6 |       7 |       8 |       9 |
|:------------|--------:|--------:|--------:|--------:|--------:|--------:|--------:|--------:|--------:|
| (3.0,1.0)   | 0.0224  | 0.15967 | 0.50071 | 0.84117 | 1       | 1       | 1       | 1       | 1       |
| (3.0,0.7)   | 0.00216 | 0.07615 | 0.50046 | 0.92394 | 1       | 1       | 1       | 1       | 1       |
| (3.0,0.5)   | 3e-05   | 0.02279 | 0.49968 | 0.9771  | 1       | 1       | 1       | 1       | 1       |
| (3.0,0.4)   | 0       | 0.00607 | 0.49931 | 0.99377 | 1       | 1       | 1       | 1       | 1       |
| (3.5,1.2)   | 0.01857 | 0.10516 | 0.33895 | 0.66145 | 0.89378 | 1       | 1       | 1       | 1       |
| (3.5,0.8)   | 0.00085 | 0.03029 | 0.26671 | 0.73305 | 0.96973 | 1       | 1       | 1       | 1       |
| (3.5,0.6)   | 2e-05   | 0.00633 | 0.20207 | 0.79744 | 0.99379 | 1       | 1       | 1       | 1       |
| (3.5,0.5)   | 0       | 0.00134 | 0.15953 | 0.84125 | 0.99868 | 1       | 1       | 1       | 1       |
| (4.0,1.5)   | 0.02282 | 0.09117 | 0.25227 | 0.50002 | 0.74785 | 0.90873 | 1       | 1       | 1       |
| (4.0,1.0)   | 0.00138 | 0.02269 | 0.15887 | 0.49955 | 0.84112 | 0.97708 | 1       | 1       | 1       |
| (4.0,0.8)   | 9e-05   | 0.00621 | 0.10544 | 0.50042 | 0.8939  | 0.99391 | 1       | 1       | 1       |
| (4.0,0.6)   | 0       | 0.0004  | 0.04821 | 0.49976 | 0.95225 | 0.99958 | 1       | 1       | 1       |
| (5.0,2.0)   | 0.02279 | 0.06698 | 0.15848 | 0.30939 | 0.50033 | 0.69183 | 0.84188 | 0.93332 | 1       |
| (5.0,1.3)   | 0.00105 | 0.01054 | 0.06202 | 0.22065 | 0.49962 | 0.77925 | 0.93851 | 0.98961 | 1       |
| (5.0,1.0)   | 3e-05   | 0.00136 | 0.02256 | 0.15797 | 0.50039 | 0.84106 | 0.97722 | 0.99857 | 1       |
| (5.0,0.8)   | 0       | 7e-05   | 0.0062  | 0.1053  | 0.50051 | 0.89419 | 0.99374 | 0.9999  | 1       |
| (7.0,3.0)   | 0.02267 | 0.04734 | 0.09107 | 0.15825 | 0.25292 | 0.36938 | 0.50006 | 0.63082 | 0.74734 |
| (7.0,2.0)   | 0.00139 | 0.00623 | 0.02255 | 0.06707 | 0.15821 | 0.30913 | 0.49994 | 0.69188 | 0.84113 |
| (7.0,1.5)   | 4e-05   | 0.00042 | 0.00381 | 0.02288 | 0.09132 | 0.25304 | 0.50011 | 0.74738 | 0.90822 |
| (7.0,1.2)   | 0       | 2e-05   | 0.00043 | 0.00622 | 0.048   | 0.20234 | 0.50016 | 0.79817 | 0.95204 |
| (9.0,4.0)   | 0.02262 | 0.04012 | 0.06677 | 0.10559 | 0.15843 | 0.22688 | 0.30808 | 0.40174 | 0.49957 |
| (9.0,2.7)   | 0.0016  | 0.00476 | 0.01306 | 0.03204 | 0.06882 | 0.13374 | 0.2294  | 0.35566 | 0.5007  |
| (9.0,2.0)   | 4e-05   | 0.00023 | 0.0013  | 0.00631 | 0.02263 | 0.06676 | 0.15864 | 0.30828 | 0.49994 |
| (9.0,1.6)   | 0       | 1e-05   | 9e-05   | 0.00094 | 0.0062  | 0.03026 | 0.1055  | 0.26564 | 0.50035 |

Suppose that an attacker knows that there are either N or N+1 AIDs in a bucket.  Suppose that the probability of either outcome is 50%. The following table shows three things (lower=1.5):
1. The probability that there is in fact N AIDs given that the bucket is suppressed.
2. The probability that there are in fact N+1 AIDs given that the bucket is reported.
3. The likelihood that the bucket is reported.
        
| (mean,sd)   | N   | Prob N AIDs (sup)   | Prob N+1 AIDs (rep)   | Prob reported   |
|:------------|:----|:--------------------|:----------------------|:----------------|
| (3.0,1.0)   | 1   | 0.544               | 1.00                  | 1/12            |
| (3.0,1.0)   | 2   | 0.627               | 0.76                  | 1/3             |
|             |     |                     |                       |                 |
| (3.0,0.7)   | 1   | 0.520               | 1.00                  | 1/26            |
| (3.0,0.7)   | 2   | 0.649               | 0.87                  | 1/3             |
|             |     |                     |                       |                 |
| (3.0,0.5)   | 1   | 0.505               | 1.00                  | 1/88            |
| (3.0,0.5)   | 2   | 0.662               | 0.96                  | 1/3             |
|             |     |                     |                       |                 |
| (3.0,0.4)   | 1   | 0.501               | 1.00                  | 1/322           |
| (3.0,0.4)   | 2   | 0.666               | 0.99                  | 1/3             |
|             |     |                     |                       |                 |
| (3.5,1.2)   | 1   | 0.527               | 1.00                  | 1/18            |
| (3.5,1.2)   | 2   | 0.575               | 0.76                  | 1/4             |
|             |     |                     |                       |                 |
| (3.5,0.8)   | 1   | 0.508               | 1.00                  | 1/66            |
| (3.5,0.8)   | 2   | 0.569               | 0.90                  | 1/6             |
|             |     |                     |                       |                 |
| (3.5,0.6)   | 1   | 0.502               | 1.00                  | 1/318           |
| (3.5,0.6)   | 2   | 0.555               | 0.97                  | 1/9             |
|             |     |                     |                       |                 |
| (3.5,0.5)   | 1   | 0.500               | 1.00                  | 1/1533          |
| (3.5,0.5)   | 2   | 0.542               | 0.99                  | 1/12            |
|             |     |                     |                       |                 |
| (4.0,1.5)   | 1   | 0.524               | 1.00                  | 1/21            |
| (4.0,1.5)   | 2   | 0.548               | 0.73                  | 1/5             |
| (4.0,1.5)   | 3   | 0.599               | 0.66                  | 1/2             |
|             |     |                     |                       |                 |
| (4.0,1.0)   | 1   | 0.505               | 1.00                  | 1/87            |
| (4.0,1.0)   | 2   | 0.537               | 0.87                  | 1/11            |
| (4.0,1.0)   | 3   | 0.627               | 0.76                  | 1/3             |
|             |     |                     |                       |                 |
| (4.0,0.8)   | 1   | 0.502               | 1.00                  | 1/326           |
| (4.0,0.8)   | 2   | 0.526               | 0.94                  | 1/17            |
| (4.0,0.8)   | 3   | 0.642               | 0.83                  | 1/3             |
|             |     |                     |                       |                 |
| (4.0,0.6)   | 1   | 0.500               | 1.00                  | 1/5025          |
| (4.0,0.6)   | 2   | 0.513               | 0.99                  | 1/41            |
| (4.0,0.6)   | 3   | 0.655               | 0.91                  | 1/3             |
|             |     |                     |                       |                 |
| (5.0,2.0)   | 1   | 0.517               | 1.00                  | 1/29            |
| (5.0,2.0)   | 2   | 0.526               | 0.70                  | 1/8             |
| (5.0,2.0)   | 3   | 0.550               | 0.66                  | 1/4             |
| (5.0,2.0)   | 4   | 0.580               | 0.62                  | 1/2             |
|             |     |                     |                       |                 |
| (5.0,1.3)   | 1   | 0.502               | 1.00                  | 1/190           |
| (5.0,1.3)   | 2   | 0.513               | 0.86                  | 1/27            |
| (5.0,1.3)   | 3   | 0.547               | 0.78                  | 1/7             |
| (5.0,1.3)   | 4   | 0.608               | 0.69                  | 1/2             |
|             |     |                     |                       |                 |
| (5.0,1.0)   | 1   | 0.501               | 1.00                  | 1/1508          |
| (5.0,1.0)   | 2   | 0.505               | 0.94                  | 1/83            |
| (5.0,1.0)   | 3   | 0.538               | 0.88                  | 1/10            |
| (5.0,1.0)   | 4   | 0.627               | 0.76                  | 1/3             |
|             |     |                     |                       |                 |
| (5.0,0.8)   | 1   | 0.499               | 1.00                  | 1/19230         |
| (5.0,0.8)   | 2   | 0.503               | 0.98                  | 1/315           |
| (5.0,0.8)   | 3   | 0.526               | 0.94                  | 1/17            |
| (5.0,0.8)   | 4   | 0.640               | 0.83                  | 1/3             |
|             |     |                     |                       |                 |
| (7.0,3.0)   | 1   | 0.513               | 1.00                  | 1/41            |
| (7.0,3.0)   | 2   | 0.512               | 0.65                  | 1/14            |
| (7.0,3.0)   | 3   | 0.519               | 0.64                  | 1/7             |
| (7.0,3.0)   | 4   | 0.530               | 0.62                  | 1/4             |
| (7.0,3.0)   | 5   | 0.542               | 0.59                  | 1/3             |
| (7.0,3.0)   | 6   | 0.556               | 0.58                  | 1/2             |
|             |     |                     |                       |                 |
| (7.0,2.0)   | 1   | 0.502               | 1.00                  | 1/317           |
| (7.0,2.0)   | 2   | 0.504               | 0.78                  | 1/68            |
| (7.0,2.0)   | 3   | 0.512               | 0.75                  | 1/22            |
| (7.0,2.0)   | 4   | 0.526               | 0.70                  | 1/8             |
| (7.0,2.0)   | 5   | 0.549               | 0.66                  | 1/4             |
| (7.0,2.0)   | 6   | 0.579               | 0.62                  | 1/2             |
|             |     |                     |                       |                 |
| (7.0,1.5)   | 1   | 0.500               | 1.00                  | 1/5154          |
| (7.0,1.5)   | 2   | 0.501               | 0.90                  | 1/451           |
| (7.0,1.5)   | 3   | 0.505               | 0.85                  | 1/75            |
| (7.0,1.5)   | 4   | 0.519               | 0.80                  | 1/17            |
| (7.0,1.5)   | 5   | 0.549               | 0.74                  | 1/5             |
| (7.0,1.5)   | 6   | 0.600               | 0.66                  | 1/2             |
|             |     |                     |                       |                 |
| (7.0,1.2)   | 1   | 0.500               | 1.00                  | 1/142857        |
| (7.0,1.2)   | 2   | 0.501               | 0.98                  | 1/4608          |
| (7.0,1.2)   | 3   | 0.501               | 0.93                  | 1/308           |
| (7.0,1.2)   | 4   | 0.510               | 0.89                  | 1/36            |
| (7.0,1.2)   | 5   | 0.543               | 0.81                  | 1/8             |
| (7.0,1.2)   | 6   | 0.614               | 0.71                  | 1/2             |
|             |     |                     |                       |                 |
| (9.0,4.0)   | 1   | 0.509               | 1.00                  | 1/49            |
| (9.0,4.0)   | 2   | 0.508               | 0.63                  | 1/18            |
| (9.0,4.0)   | 3   | 0.511               | 0.61                  | 1/11            |
| (9.0,4.0)   | 4   | 0.516               | 0.60                  | 1/7             |
| (9.0,4.0)   | 5   | 0.522               | 0.59                  | 1/5             |
| (9.0,4.0)   | 6   | 0.528               | 0.58                  | 1/3             |
| (9.0,4.0)   | 7   | 0.536               | 0.57                  | 1/2             |
| (9.0,4.0)   | 8   | 0.545               | 0.55                  | 1/2             |
|             |     |                     |                       |                 |
| (9.0,2.7)   | 1   | 0.500               | 1.00                  | 1/421           |
| (9.0,2.7)   | 2   | 0.503               | 0.74                  | 1/112           |
| (9.0,2.7)   | 3   | 0.506               | 0.71                  | 1/44            |
| (9.0,2.7)   | 4   | 0.510               | 0.69                  | 1/19            |
| (9.0,2.7)   | 5   | 0.518               | 0.66                  | 1/9             |
| (9.0,2.7)   | 6   | 0.529               | 0.63                  | 1/5             |
| (9.0,2.7)   | 7   | 0.545               | 0.61                  | 1/3             |
| (9.0,2.7)   | 8   | 0.563               | 0.58                  | 1/2             |
|             |     |                     |                       |                 |
| (9.0,2.0)   | 1   | 0.500               | 1.00                  | 1/8620          |
| (9.0,2.0)   | 2   | 0.500               | 0.86                  | 1/1291          |
| (9.0,2.0)   | 3   | 0.502               | 0.82                  | 1/263           |
| (9.0,2.0)   | 4   | 0.505               | 0.79                  | 1/68            |
| (9.0,2.0)   | 5   | 0.510               | 0.74                  | 1/22            |
| (9.0,2.0)   | 6   | 0.526               | 0.70                  | 1/8             |
| (9.0,2.0)   | 7   | 0.548               | 0.66                  | 1/4             |
| (9.0,2.0)   | 8   | 0.581               | 0.62                  | 1/2             |
|             |     |                     |                       |                 |
| (9.0,1.6)   | 1   | 0.500               | 1.00                  | 1/1000000       |
| (9.0,1.6)   | 2   | 0.500               | 0.91                  | 1/22222         |
| (9.0,1.6)   | 3   | 0.500               | 0.89                  | 1/2105          |
| (9.0,1.6)   | 4   | 0.501               | 0.88                  | 1/273           |
| (9.0,1.6)   | 5   | 0.506               | 0.83                  | 1/54            |
| (9.0,1.6)   | 6   | 0.519               | 0.78                  | 1/14            |
| (9.0,1.6)   | 7   | 0.548               | 0.71                  | 1/5             |
| (9.0,1.6)   | 8   | 0.594               | 0.65                  | 1/2             |
|             |     |                     |                       |                 |

Given the count of distinct AIDs in a bucket, what is the probability that the bucket will be reported (not suppressed) (`lower=0`):
        
| (mean,sd)   |       2 |       3 |       4 |       5 |       6 |       7 |       8 |       9 |      10 |
|:------------|--------:|--------:|--------:|--------:|--------:|--------:|--------:|--------:|--------:|
| (4.0,1.0)   | 0.02299 | 0.15914 | 0.50003 | 0.84112 | 1       | 1       | 1       | 1       | 1       |
| (4.0,0.7)   | 0.00215 | 0.07658 | 0.49978 | 0.92376 | 1       | 1       | 1       | 1       | 1       |
| (4.0,0.5)   | 3e-05   | 0.0226  | 0.50067 | 0.97718 | 1       | 1       | 1       | 1       | 1       |
| (4.0,0.4)   | 0       | 0.0062  | 0.50016 | 0.99382 | 1       | 1       | 1       | 1       | 1       |
| (4.5,1.2)   | 0.01866 | 0.10531 | 0.3387  | 0.66203 | 0.89434 | 1       | 1       | 1       | 1       |
| (4.5,0.8)   | 0.00086 | 0.03037 | 0.26571 | 0.73453 | 0.96972 | 1       | 1       | 1       | 1       |
| (4.5,0.6)   | 1e-05   | 0.00626 | 0.20213 | 0.79739 | 0.99373 | 1       | 1       | 1       | 1       |
| (4.5,0.5)   | 0       | 0.00132 | 0.15832 | 0.8422  | 0.99869 | 1       | 1       | 1       | 1       |
| (5.0,1.5)   | 0.02251 | 0.09141 | 0.25219 | 0.50036 | 0.74731 | 0.90838 | 1       | 1       | 1       |
| (5.0,1.0)   | 0.00134 | 0.02254 | 0.15871 | 0.49921 | 0.84176 | 0.97699 | 1       | 1       | 1       |
| (5.0,0.8)   | 9e-05   | 0.00614 | 0.10552 | 0.49985 | 0.89464 | 0.99386 | 1       | 1       | 1       |
| (5.0,0.6)   | 0       | 0.00042 | 0.0477  | 0.50072 | 0.95219 | 0.99954 | 1       | 1       | 1       |
| (6.0,2.0)   | 0.02287 | 0.06707 | 0.15896 | 0.30895 | 0.49927 | 0.69171 | 0.84121 | 0.93343 | 1       |
| (6.0,1.3)   | 0.00106 | 0.01038 | 0.06144 | 0.2208  | 0.50007 | 0.77945 | 0.9379  | 0.98923 | 1       |
| (6.0,1.0)   | 3e-05   | 0.00137 | 0.02275 | 0.15855 | 0.50047 | 0.84128 | 0.97728 | 0.99864 | 1       |
| (6.0,0.8)   | 0       | 9e-05   | 0.00618 | 0.10626 | 0.50002 | 0.89429 | 0.99373 | 0.99991 | 1       |
| (8.0,3.0)   | 0.02274 | 0.04787 | 0.09139 | 0.15811 | 0.25268 | 0.3688  | 0.5005  | 0.63082 | 0.74764 |
| (8.0,2.0)   | 0.00138 | 0.00633 | 0.02274 | 0.06662 | 0.15877 | 0.30841 | 0.50003 | 0.69104 | 0.8412  |
| (8.0,1.5)   | 3e-05   | 0.00044 | 0.00383 | 0.02286 | 0.0912  | 0.25181 | 0.49994 | 0.7468  | 0.90866 |
| (8.0,1.2)   | 0       | 2e-05   | 0.00046 | 0.00629 | 0.04801 | 0.20186 | 0.49948 | 0.7976  | 0.95225 |
| (10.0,4.0)  | 0.02262 | 0.03985 | 0.06681 | 0.10564 | 0.15821 | 0.22689 | 0.30911 | 0.40147 | 0.50015 |
| (10.0,2.7)  | 0.00151 | 0.00475 | 0.01313 | 0.03247 | 0.06909 | 0.13321 | 0.22949 | 0.35507 | 0.49965 |
| (10.0,2.0)  | 3e-05   | 0.00025 | 0.00139 | 0.00623 | 0.02267 | 0.06643 | 0.15885 | 0.30746 | 0.50091 |
| (10.0,1.6)  | 0       | 0       | 9e-05   | 0.00096 | 0.00626 | 0.03047 | 0.10549 | 0.26672 | 0.50044 |

Suppose that an attacker knows that there are either N or N+1 AIDs in a bucket.  Suppose that the probability of either outcome is 50%. The following table shows three things (lower=2.5):
1. The probability that there is in fact N AIDs given that the bucket is suppressed.
2. The probability that there are in fact N+1 AIDs given that the bucket is reported.
3. The likelihood that the bucket is reported.
        
| (mean,sd)   | N   | Prob N AIDs (sup)   | Prob N+1 AIDs (rep)   | Prob reported   |
|:------------|:----|:--------------------|:----------------------|:----------------|
| (4.0,1.0)   | 1   | 0.500               | 1                     | 1000000++       |
| (4.0,1.0)   | 2   | 0.543               | 1.00                  | 1/12            |
| (4.0,1.0)   | 3   | 0.627               | 0.76                  | 1/3             |
|             |     |                     |                       |                 |
| (4.0,0.7)   | 1   | 0.501               | 1                     | 1000000++       |
| (4.0,0.7)   | 2   | 0.519               | 1.00                  | 1/26            |
| (4.0,0.7)   | 3   | 0.650               | 0.87                  | 1/3             |
|             |     |                     |                       |                 |
| (4.0,0.5)   | 1   | 0.501               | 1                     | 1000000++       |
| (4.0,0.5)   | 2   | 0.506               | 1.00                  | 1/88            |
| (4.0,0.5)   | 3   | 0.662               | 0.96                  | 1/3             |
|             |     |                     |                       |                 |
| (4.0,0.4)   | 1   | 0.500               | 1                     | 1000000++       |
| (4.0,0.4)   | 2   | 0.502               | 1.00                  | 1/318           |
| (4.0,0.4)   | 3   | 0.665               | 0.99                  | 1/3             |
|             |     |                     |                       |                 |
| (4.5,1.2)   | 1   | 0.500               | 1                     | 1000000++       |
| (4.5,1.2)   | 2   | 0.528               | 1.00                  | 1/18            |
| (4.5,1.2)   | 3   | 0.575               | 0.76                  | 1/4             |
|             |     |                     |                       |                 |
| (4.5,0.8)   | 1   | 0.500               | 1                     | 1000000++       |
| (4.5,0.8)   | 2   | 0.508               | 1.00                  | 1/66            |
| (4.5,0.8)   | 3   | 0.569               | 0.90                  | 1/6             |
|             |     |                     |                       |                 |
| (4.5,0.6)   | 1   | 0.500               | 1                     | 1000000++       |
| (4.5,0.6)   | 2   | 0.501               | 1.00                  | 1/330           |
| (4.5,0.6)   | 3   | 0.555               | 0.97                  | 1/9             |
|             |     |                     |                       |                 |
| (4.5,0.5)   | 1   | 0.500               | 1                     | 1000000++       |
| (4.5,0.5)   | 2   | 0.500               | 1.00                  | 1/1398          |
| (4.5,0.5)   | 3   | 0.543               | 0.99                  | 1/12            |
|             |     |                     |                       |                 |
| (5.0,1.5)   | 1   | 0.500               | 1                     | 1000000++       |
| (5.0,1.5)   | 2   | 0.524               | 1.00                  | 1/21            |
| (5.0,1.5)   | 3   | 0.547               | 0.73                  | 1/5             |
| (5.0,1.5)   | 4   | 0.599               | 0.66                  | 1/2             |
|             |     |                     |                       |                 |
| (5.0,1.0)   | 1   | 0.500               | 1                     | 1000000++       |
| (5.0,1.0)   | 2   | 0.506               | 1.00                  | 1/86            |
| (5.0,1.0)   | 3   | 0.537               | 0.88                  | 1/11            |
| (5.0,1.0)   | 4   | 0.627               | 0.76                  | 1/3             |
|             |     |                     |                       |                 |
| (5.0,0.8)   | 1   | 0.499               | 1                     | 1000000++       |
| (5.0,0.8)   | 2   | 0.502               | 1.00                  | 1/315           |
| (5.0,0.8)   | 3   | 0.527               | 0.94                  | 1/17            |
| (5.0,0.8)   | 4   | 0.641               | 0.82                  | 1/3             |
|             |     |                     |                       |                 |
| (5.0,0.6)   | 1   | 0.500               | 1                     | 1000000++       |
| (5.0,0.6)   | 2   | 0.501               | 1.00                  | 1/4950          |
| (5.0,0.6)   | 3   | 0.513               | 0.99                  | 1/41            |
| (5.0,0.6)   | 4   | 0.656               | 0.91                  | 1/3             |
|             |     |                     |                       |                 |
| (6.0,2.0)   | 1   | 0.500               | 1                     | 1000000++       |
| (6.0,2.0)   | 2   | 0.517               | 1.00                  | 1/29            |
| (6.0,2.0)   | 3   | 0.526               | 0.70                  | 1/8             |
| (6.0,2.0)   | 4   | 0.548               | 0.66                  | 1/4             |
| (6.0,2.0)   | 5   | 0.581               | 0.62                  | 1/2             |
|             |     |                     |                       |                 |
| (6.0,1.3)   | 1   | 0.500               | 1                     | 1000000++       |
| (6.0,1.3)   | 2   | 0.502               | 1.00                  | 1/188           |
| (6.0,1.3)   | 3   | 0.514               | 0.86                  | 1/27            |
| (6.0,1.3)   | 4   | 0.545               | 0.78                  | 1/7             |
| (6.0,1.3)   | 5   | 0.609               | 0.69                  | 1/2             |
|             |     |                     |                       |                 |
| (6.0,1.0)   | 1   | 0.501               | 1                     | 1000000++       |
| (6.0,1.0)   | 2   | 0.500               | 1.00                  | 1/1522          |
| (6.0,1.0)   | 3   | 0.507               | 0.94                  | 1/82            |
| (6.0,1.0)   | 4   | 0.537               | 0.87                  | 1/11            |
| (6.0,1.0)   | 5   | 0.627               | 0.76                  | 1/3             |
|             |     |                     |                       |                 |
| (6.0,0.8)   | 1   | 0.499               | 1                     | 1000000++       |
| (6.0,0.8)   | 2   | 0.500               | 1.00                  | 1/20833         |
| (6.0,0.8)   | 3   | 0.502               | 0.99                  | 1/316           |
| (6.0,0.8)   | 4   | 0.526               | 0.95                  | 1/17            |
| (6.0,0.8)   | 5   | 0.642               | 0.82                  | 1/3             |
|             |     |                     |                       |                 |
| (8.0,3.0)   | 1   | 0.500               | 1                     | 1000000++       |
| (8.0,3.0)   | 2   | 0.512               | 1.00                  | 1/42            |
| (8.0,3.0)   | 3   | 0.511               | 0.65                  | 1/14            |
| (8.0,3.0)   | 4   | 0.519               | 0.64                  | 1/7             |
| (8.0,3.0)   | 5   | 0.529               | 0.61                  | 1/4             |
| (8.0,3.0)   | 6   | 0.543               | 0.60                  | 1/3             |
| (8.0,3.0)   | 7   | 0.557               | 0.58                  | 1/2             |
|             |     |                     |                       |                 |
| (8.0,2.0)   | 1   | 0.501               | 1                     | 1000000++       |
| (8.0,2.0)   | 2   | 0.501               | 1.00                  | 1/324           |
| (8.0,2.0)   | 3   | 0.503               | 0.79                  | 1/71            |
| (8.0,2.0)   | 4   | 0.511               | 0.75                  | 1/22            |
| (8.0,2.0)   | 5   | 0.526               | 0.70                  | 1/8             |
| (8.0,2.0)   | 6   | 0.549               | 0.66                  | 1/4             |
| (8.0,2.0)   | 7   | 0.581               | 0.62                  | 1/2             |
|             |     |                     |                       |                 |
| (8.0,1.5)   | 1   | 0.499               | 1                     | 1000000++       |
| (8.0,1.5)   | 2   | 0.500               | 1.00                  | 1/4504          |
| (8.0,1.5)   | 3   | 0.500               | 0.90                  | 1/470           |
| (8.0,1.5)   | 4   | 0.505               | 0.85                  | 1/75            |
| (8.0,1.5)   | 5   | 0.518               | 0.80                  | 1/17            |
| (8.0,1.5)   | 6   | 0.549               | 0.73                  | 1/5             |
| (8.0,1.5)   | 7   | 0.600               | 0.66                  | 1/2             |
|             |     |                     |                       |                 |
| (8.0,1.2)   | 1   | 0.500               | 1                     | 1000000++       |
| (8.0,1.2)   | 2   | 0.499               | 1.00                  | 1/142857        |
| (8.0,1.2)   | 3   | 0.500               | 0.98                  | 1/4405          |
| (8.0,1.2)   | 4   | 0.500               | 0.94                  | 1/297           |
| (8.0,1.2)   | 5   | 0.510               | 0.88                  | 1/37            |
| (8.0,1.2)   | 6   | 0.545               | 0.81                  | 1/7             |
| (8.0,1.2)   | 7   | 0.615               | 0.71                  | 1/2             |
|             |     |                     |                       |                 |
| (10.0,4.0)  | 1   | 0.500               | 1                     | 1000000++       |
| (10.0,4.0)  | 2   | 0.511               | 1.00                  | 1/49            |
| (10.0,4.0)  | 3   | 0.507               | 0.62                  | 1/18            |
| (10.0,4.0)  | 4   | 0.511               | 0.61                  | 1/11            |
| (10.0,4.0)  | 5   | 0.515               | 0.60                  | 1/7             |
| (10.0,4.0)  | 6   | 0.521               | 0.59                  | 1/5             |
| (10.0,4.0)  | 7   | 0.527               | 0.58                  | 1/3             |
| (10.0,4.0)  | 8   | 0.535               | 0.57                  | 1/2             |
| (10.0,4.0)  | 9   | 0.545               | 0.56                  | 1/2             |
|             |     |                     |                       |                 |
| (10.0,2.7)  | 1   | 0.500               | 1                     | 1000000++       |
| (10.0,2.7)  | 2   | 0.501               | 1.00                  | 1/418           |
| (10.0,2.7)  | 3   | 0.501               | 0.73                  | 1/109           |
| (10.0,2.7)  | 4   | 0.504               | 0.71                  | 1/44            |
| (10.0,2.7)  | 5   | 0.509               | 0.68                  | 1/19            |
| (10.0,2.7)  | 6   | 0.518               | 0.65                  | 1/9             |
| (10.0,2.7)  | 7   | 0.529               | 0.63                  | 1/5             |
| (10.0,2.7)  | 8   | 0.543               | 0.61                  | 1/3             |
| (10.0,2.7)  | 9   | 0.564               | 0.58                  | 1/2             |
|             |     |                     |                       |                 |
| (10.0,2.0)  | 1   | 0.499               | 1                     | 1000000++       |
| (10.0,2.0)  | 2   | 0.501               | 1.00                  | 1/9174          |
| (10.0,2.0)  | 3   | 0.500               | 0.83                  | 1/1310          |
| (10.0,2.0)  | 4   | 0.501               | 0.83                  | 1/264           |
| (10.0,2.0)  | 5   | 0.505               | 0.79                  | 1/68            |
| (10.0,2.0)  | 6   | 0.511               | 0.75                  | 1/22            |
| (10.0,2.0)  | 7   | 0.526               | 0.70                  | 1/8             |
| (10.0,2.0)  | 8   | 0.549               | 0.66                  | 1/4             |
| (10.0,2.0)  | 9   | 0.579               | 0.62                  | 1/2             |
|             |     |                     |                       |                 |
| (10.0,1.6)  | 1   | 0.499               | 1                     | 1000000++       |
| (10.0,1.6)  | 2   | 0.500               | 1.00                  | 1/500000        |
| (10.0,1.6)  | 3   | 0.499               | 0.96                  | 1/21739         |
| (10.0,1.6)  | 4   | 0.501               | 0.91                  | 1/2070          |
| (10.0,1.6)  | 5   | 0.500               | 0.88                  | 1/287           |
| (10.0,1.6)  | 6   | 0.507               | 0.83                  | 1/54            |
| (10.0,1.6)  | 7   | 0.519               | 0.78                  | 1/14            |
| (10.0,1.6)  | 8   | 0.550               | 0.72                  | 1/5             |
| (10.0,1.6)  | 9   | 0.595               | 0.65                  | 1/2             |
|             |     |                     |                       |                 |
