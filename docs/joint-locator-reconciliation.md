---
layout: default
title: Joint Locator Reconciliation - Automation Sandbox
---

# Joint Locator Reconciliation

Joint locator reconciliation is an opt-in batch guard for callers that resolve several
stale locators against the same captured UI tree. It prevents two independently accepted
locator heals from silently taking ownership of the same live element. Existing
`SelfHealingResolver.Resolve` and `ResolveAsync` calls are unchanged.

## Public API and compatibility

Callers create one `BatchHealingRequest` per stale locator and pass the requests with one
shared `UiElementInfo` tree to `ResolveBatch` or `ResolveBatchAsync`. Results preserve input
order and expose the ordinary `HealResult` plus the reconciliation disposition and the
candidate identity used for ownership.

```csharp
var batch = await SelfHealingResolver.ResolveBatchAsync(
    new[]
    {
        new BatchHealingRequest("checkout.submit", staleSubmit),
        new BatchHealingRequest("checkout.cancel", staleCancel),
    },
    currentTree,
    llmProviders);

foreach (var item in batch.Items)
{
    if (item.Result.IsConfident)
    {
        // Persist or execute only the reconciled match.
    }
}
```

The batch API is additive and opt-in. It does not change the score, evidence, candidate
margin, hallucination guard, or LLM consensus rules used by either single-locator method.

## Deterministic assignment contract

Each candidate receives an opaque identity from its zero-based pre-order path in the exact
captured tree supplied to the batch call (`r`, `r/0`, `r/0/2`, and so on). The path is
unique within that snapshot and does not depend on `AutomationId`, which may be empty or
duplicated. It is batch/report telemetry, not a locator intended for reuse against a later
tree.

Only results already accepted by the existing resolver gates may claim a candidate. The
batch guard never promotes a runner-up and never creates a match. An unmatched assignment
has utility `0`; an accepted claim's assignment utility is its structural candidate score.
For every candidate:

- one accepted claimant is preserved;
- when the leading claim exceeds the runner-up by at least `MinimumCandidateMargin`, the
  leader wins and the remaining claims are declined;
- when that ownership margin is smaller, every claimant is declined for manual review;
- ordinal locator-key ordering makes output and diagnostics deterministic, but never breaks
  an ambiguous tie.

This is top-claim ownership reconciliation, not a global runner-up optimizer. That narrower
objective is deliberate: the frozen #141/#143 dataset showed it could remove contested
false heals without introducing a new accepted match.

## LLM, failures, and atomicity

`ResolveBatchAsync` resolves each locator with the existing per-locator provider shortlist,
hallucination guard, and `MinimumConsensusVotes`. Confidence remains informational and is
not an acceptance gate. Providers are invoked per locator in input order; one provider's
failure is retained in that locator's normal telemetry and does not erase other locator
results. Reconciliation runs only after every locator has an independent result.

Batch resolution itself is side-effect free: it does not update a locator repository,
write a healing report, or execute a healed action. The complete input is validated before
resolution begins. Invalid or duplicate locator keys fail the call before provider work;
cancellation throws rather than returning a partial batch. Callers decide whether and how
to commit the returned reconciled results. This keeps persistence atomicity at the caller's
transaction boundary instead of pretending that unrelated repository, report, and UI
action backends share one transaction.

Healing report schema v8 records nullable `CandidateIdentity` and
`ReconciliationDisposition` fields. Older reports upgrade with `null`, meaning the writer
did not observe batch reconciliation. A reconciliation decline uses the explicit
`ownership-conflict` outcome while retaining the independently proposed snapshot for audit.

## Limitation

One-to-one ownership can detect only contested claims. If a deleted locator is the sole
claimant for a structurally similar surviving element, batch reconciliation preserves that
accepted result. The frozen HandBrake/ShareX evaluation left all 15 such uncontested false
heals unchanged. The feature is therefore a targeted collision guard, not an absence
detector or a general false-heal solution.

---

# Birleşik Locator Uzlaştırması

Birleşik locator uzlaştırması, birkaç eski locator'ı aynı yakalanmış UI ağacına karşı çözen
çağıranlar için isteğe bağlı bir batch korumasıdır. Bağımsız olarak kabul edilmiş iki
locator iyileştirmesinin aynı canlı elementin sahipliğini sessizce almasını engeller.
Mevcut `SelfHealingResolver.Resolve` ve `ResolveAsync` çağrıları değişmez.

## Public API ve uyumluluk

Çağıran, her eski locator için bir `BatchHealingRequest` oluşturur ve isteklerle ortak
`UiElementInfo` ağacını `ResolveBatch` veya `ResolveBatchAsync` metoduna verir. Sonuçlar
girdi sırasını korur; olağan `HealResult` ile uzlaştırma kararını ve sahiplikte kullanılan
aday kimliğini sunar.

Batch API eklemeli ve isteğe bağlıdır. Tekli locator metotlarının skor, kanıt, aday marjı,
halüsinasyon koruması veya LLM uzlaşı kurallarını değiştirmez.

## Deterministik atama sözleşmesi

Her aday, batch çağrısına verilen ağacın sıfır tabanlı pre-order yolundan opak bir kimlik
alır (`r`, `r/0`, `r/0/2` gibi). Bu yol yalnızca o snapshot içinde benzersizdir ve boş ya da
tekrarlı olabilen `AutomationId` değerine dayanmaz. Daha sonraki bir ağaçta yeniden
kullanılacak locator değil, batch/rapor telemetrisidir.

Yalnızca mevcut resolver gate'leri tarafından zaten kabul edilmiş sonuçlar aday talep
edebilir. Batch koruması runner-up adayı asla terfi ettirmez ve yeni eşleşme üretmez.
Eşleşmemenin utility değeri `0`, kabul edilmiş talebin atama utility değeri yapısal aday
skorudur. Her aday için:

- tek kabul edilmiş talep korunur;
- lider talep runner-up'ı en az `MinimumCandidateMargin` kadar geçerse lider kazanır ve
  kalan talepler reddedilir;
- sahiplik marjı daha küçükse tüm talepler manuel incelemeye bırakılır;
- ordinal locator-key sırası çıktı ve tanıları deterministik yapar, fakat belirsiz eşitliği
  çözmek için kullanılmaz.

Bu davranış global bir runner-up optimizasyonu değil, yalnızca top-claim sahiplik
uzlaştırmasıdır. #141/#143 sabit veri kümesinde yeni kabul edilmiş eşleşme üretmeden
tartışmalı false-heal'leri kaldıran davranış budur.

## LLM, hatalar ve atomiklik

`ResolveBatchAsync`, her locator'ı mevcut per-locator provider shortlist'i, halüsinasyon
koruması ve `MinimumConsensusVotes` ile çözer. Confidence yalnızca bilgilendiricidir ve
kabul gate'i değildir. Provider'lar locator başına ve girdi sırasında çağrılır; bir provider
hatası o locator'ın olağan telemetrisinde saklanır ve diğer locator sonuçlarını silmez.
Uzlaştırma ancak tüm locator'lar bağımsız sonuç ürettikten sonra çalışır.

Batch çözümleme yan etkisizdir: locator deposunu güncellemez, iyileştirme raporu yazmaz ve
iyileştirilmiş action çalıştırmaz. Tüm girdi çözüm başlamadan doğrulanır. Geçersiz veya
tekrarlı locator anahtarları provider çalışmasından önce çağrıyı durdurur; iptal, kısmi batch
döndürmek yerine exception üretir. Dönen sonuçların nasıl ve hangi transaction sınırında
kaydedileceğine çağıran karar verir.

İyileştirme raporu şema v8, nullable `CandidateIdentity` ve
`ReconciliationDisposition` alanlarını kaydeder. Eski raporlar `null` ile yükseltilir; bu,
yazan sürümün batch uzlaştırmasını gözlemlemediği anlamına gelir. Uzlaştırma reddi, bağımsız
önerilen snapshot'ı denetim için korurken açık `ownership-conflict` sonucunu kullanır.

## Sınır

Bire bir sahiplik yalnızca tartışmalı talepleri algılayabilir. Silinmiş bir locator,
yapısal olarak benzer kalan bir elementin tek talepçisiyse batch uzlaştırması kabul edilmiş
sonucu korur. Sabit HandBrake/ShareX değerlendirmesinde bu tür 15 tartışmasız false-heal'in
tamamı değişmeden kalmıştır. Dolayısıyla özellik hedefli bir çakışma korumasıdır; absence
detector veya genel false-heal çözümü değildir.
