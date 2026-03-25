\# APPROVAL\_CRITERIA.md



\## Default thresholds (configurable per strategy instance in DB)



| Metric | Default threshold | Override |

|---|---|---|

| Backtest CAGR | >= 20% | approval\_config.min\_cagr |

| Backtest max drawdown | <= 20% | approval\_config.max\_drawdown\_pct |

| Forward test min days | >= 15 days | approval\_config.min\_forward\_test\_days |

| Forward test win rate | >= 40% | approval\_config.min\_win\_rate |

| Manual approval | Required always | Cannot be skipped |



\## Approval flow

1\. run backtest -> metrics stored in backtest\_results

2\. run forward test -> metrics stored in forward\_test\_results

3\. ApprovalService.EvaluateAsync() checks all automated thresholds

4\. if all pass -> approval\_ready flag set on strategy\_instance

5\. trader reviews results in UI

6\. trader clicks Approve -> POST /api/v1/strategy-instances/{id}/approve

7\. approval record written to strategy\_approvals

8\. audit\_log entry written

9\. strategy\_instance.status -> APPROVED

10\. live deployment now allowed



\## Re-approval triggers

\- strategy config changed

\- risk profile changed

\- approval threshold config changed

\- manual reset by admin



\## API

POST /api/v1/strategy-instances/{id}/approve

GET /api/v1/strategy-instances/{id}/approval-status

GET /api/v1/strategy-instances/{id}/approval-history



