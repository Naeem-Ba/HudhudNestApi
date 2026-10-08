# STAGING-TEST-DATA

| Item | Value |
|---|---|
| Test batch id | HUDHUDNEST-STAGING-QA-2026-10 (carried in account last names and in each listing description suffix; the schema has no batch column) |
| Environment | Staging (API ec17ea9) |
| Accounts | 103: 100 owners (index 001-100), 1 probe (index 900), 2 repro (901, 902) |
| Listings | 100 batch listings (one per owner) + probe listing + 2 repro listings; 2 pre-existing Staging listings untouched |
| Images | 411 in batch listings; image-count distribution {"0":3,"1":11,"3":20,"4":40,"6":20,"10":6} |
| Cities | {"Damascus":9,"Rural Damascus":9,"Aleppo":9,"Homs":9,"Hama":8,"Latakia":8,"Tartus":8,"Deir ez-Zor":8,"Raqqa":8,"Idlib":8,"Daraa":8,"As-Suwayda":8} |
| Property types | {"apartment":30,"villa":10,"studio":5,"house":5,"residential-land":5,"hotel-apartment":5,"agricultural-land":5,"shop":10,"office":5,"commercial-land":5,"warehouse":5,"industrial-land":5,"hotel":5} |
| Listing types | {"ForRent":30,"ForSale":55,"ForRentAndSale":15} |
| Currencies | {"USD":84,"EUR":11,"SYP":5} |
| Languages of content | {"en":20,"ar":70,"de":10} |
| Configuration | Free plan per owner; fixed Staging OTP mechanism; password and secrets NOT recorded |
| Cleanup status | NOT performed (data retained for manual inspection) |
| Notable test objects | probe listing 5f282dc5-8285-4174-b1d6-70f7fdc340ae (stuck without upload ability, STG-002); repro listings {"901":"ddf26b1e-8819-4e92-84ab-ccbf06a8db9a","902":"bd0b5beb-699c-4f86-a60b-e3dee66a468e"} |

## Created listing IDs

| # | Listing id | Owner user id | City | Type | Listing type | Currency | Language | Images |
|---:|---|---|---|---|---|---|---|---:|
| 3 | 336b8818-cb64-4097-893e-d8fa58ea6d3f | 2cdaf85b-8a60-4e29-ae44-98a5a3d20338 | Aleppo | apartment | ForRent | USD | en | 1 |
| 1 | 4573858e-b37c-4fd1-8cae-d8b5d85ffb8f | 2e31124e-34de-40b7-be32-b584c60b719b | Damascus | apartment | ForRent | USD | ar | 4 |
| 2 | 4ea45a9e-7c67-4f16-8089-8aaf19cfc1c0 | 841ea4dc-edd2-44fa-837e-4aed3e49cd8e | Rural Damascus | apartment | ForSale | USD | ar | 6 |
| 4 | 97ef6cdc-a079-4cf4-8808-8ebca144474c | 37c4890e-0ea7-4ff9-a7b2-fa63074ca8c8 | Homs | apartment | ForSale | USD | ar | 4 |
| 6 | 154add1d-b7df-4ad6-bdff-e446777e862d | 9c189dbe-c30a-45e6-a8ce-3fc98796f380 | Latakia | apartment | ForRent | USD | ar | 3 |
| 5 | 4d2f9b52-e3d4-4af1-8c74-1014af4949d4 | 25e1371d-36a4-4752-9f78-98bc66fcac25 | Hama | apartment | ForRentAndSale | USD | de | 6 |
| 7 | 8e5a9053-5b77-4ace-985d-15b32dc41f8f | 09203f64-dc3c-494d-8068-dd3681e94544 | Tartus | villa | ForSale | USD | ar | 4 |
| 9 | a63dfd4f-4c31-4fc2-98ca-7cd0c2ed90a7 | b63a7bff-dc24-4bb3-ae2e-141bcb621a76 | Raqqa | studio | ForRent | EUR | ar | 3 |
| 8 | 9d94a8a2-0a22-4f15-bd78-6a5991992aa6 | 75a0a7e0-e1ee-492b-b65f-c8e5d1f739aa | Deir ez-Zor | house | ForRentAndSale | USD | en | 10 |
| 11 | 26e98ae9-009b-4ae7-bb6b-725eecbef536 | d6d3a7b7-5d82-456a-9e2a-8e3a7af15a30 | Daraa | residential-land | ForSale | USD | ar | 1 |
| 10 | 12d0daad-46d2-47be-b58d-fb686169dedd | d67e780e-07e5-43b1-913d-2d0264cfa5ea | Idlib | hotel-apartment | ForSale | USD | ar | 4 |
| 12 | 606d59ee-a5cd-48eb-91c8-17d466213c6d | 0aeda9db-fdd3-4758-a36b-dd486b052a49 | As-Suwayda | agricultural-land | ForSale | USD | ar | 4 |
| 13 | 68c2e0b8-46d2-4ea7-b5a2-476640e98498 | e46bd219-72d9-4187-af2a-f8c12682688c | Damascus | shop | ForRent | USD | en | 6 |
| 14 | 158a408c-1bbf-41bd-a993-5124e8c60f71 | da17f412-6fe4-43c9-8c41-555f650f4c6e | Rural Damascus | shop | ForSale | USD | ar | 3 |
| 15 | bdb90cb2-110b-4ea6-8abc-25f5aebe1334 | d9450e10-00e0-43a9-bb0f-de185308e0fb | Aleppo | office | ForRentAndSale | USD | de | 4 |
| 17 | ec63e01b-fb10-432a-90ef-a28f7ab6b362 | 113aa2df-1611-4270-affd-5b271387e582 | Hama | commercial-land | ForSale | SYP | ar | 3 |
| 16 | d7e18c28-821f-4966-98d9-cb6115891380 | 253d1c27-efe9-4a3d-89b4-2d75d75c1e2b | Homs | warehouse | ForRent | USD | ar | 6 |
| 18 | 3719c8ad-b85e-4191-ba87-5a14e7420455 | 687688e3-add3-4f81-a5cb-a5c31aef4c35 | Latakia | industrial-land | ForSale | EUR | en | 4 |
| 19 | 072223f7-60e6-4e6d-a043-fc6542dd9c54 | 9b56a5ed-e76c-4046-bdcc-299558e11abf | Tartus | hotel | ForSale | USD | ar | 0 |
| 20 | 36c870c2-54b2-4371-be24-048b9c6390f8 | 7c06095d-8d90-4ee2-b11a-1eefaf79212d | Deir ez-Zor | villa | ForSale | USD | ar | 4 |
| 21 | 83183d58-ef00-4863-addc-7abb89eaf879 | 2822dfab-e2da-406e-a97b-0289aa7916ee | Raqqa | apartment | ForRent | USD | ar | 6 |
| 22 | c4130d51-256f-44fb-8958-007849ee2a4e | 40600e49-446f-4190-af48-b5d647abda21 | Idlib | apartment | ForSale | USD | ar | 3 |
| 23 | 932a84b3-b9c2-425b-a63d-584b6396d241 | 0c49ebfd-90f5-4db6-aecc-d834914a4025 | Daraa | apartment | ForRent | USD | en | 4 |
| 24 | 240629a5-fa73-4852-9772-a086e7d22a9c | 334f7ff8-5996-46b9-af8d-194bf6d7e992 | As-Suwayda | apartment | ForSale | USD | ar | 6 |
| 25 | 3984d424-b12e-43f3-8b0c-f60e0be03f81 | 7717df2e-f28c-43ba-bb2b-5e624885bc68 | Damascus | apartment | ForRentAndSale | USD | de | 3 |
| 26 | a33928c3-8533-4614-b398-8ea71b4e84b2 | 4c596122-8f3d-4529-8792-f48dce99cd3a | Rural Damascus | apartment | ForRent | USD | ar | 4 |
| 28 | e4449ac6-675d-4375-805e-7d2c6b801494 | d3fad847-73b3-4847-886b-f9c88d370063 | Homs | house | ForRentAndSale | USD | en | 4 |
| 29 | 8512809b-e971-4e7f-9001-ea290be80f33 | 60b74c72-f1f8-46d5-ae05-03f30a414f52 | Hama | studio | ForRent | USD | ar | 4 |
| 27 | 0fe5b4c0-1e25-47a2-a115-c3d43e723a4b | f150b518-33da-4fb4-a4f7-3d0adc577216 | Aleppo | villa | ForSale | EUR | ar | 10 |
| 30 | 947c8c2f-0ecc-4538-8cbd-d828ee81a85e | 0b78e4b5-5d33-4c27-843b-60b0c376c77e | Latakia | hotel-apartment | ForSale | USD | ar | 1 |
| 31 | 5379919f-6bac-40e9-905c-9196f394d176 | d57fb503-ae5c-443c-a8e4-5df49ba3d1a9 | Tartus | residential-land | ForSale | USD | ar | 4 |
| 32 | 83eb182f-dfb2-4c9e-8461-0eba9c07d81c | 9d35a575-1c91-4fa6-a578-7eb935106d3d | Deir ez-Zor | agricultural-land | ForSale | USD | ar | 6 |
| 33 | 0d319ebd-f00e-4bae-a084-c14cf0e5e0b6 | 7a31a7d5-2050-46fe-9afa-fcf0469acdb4 | Raqqa | shop | ForRent | USD | en | 3 |
| 34 | 51b3125a-0b3f-4005-adb7-74a950e98d2d | 24d8c294-8bc0-4d62-a679-0dd28c988749 | Idlib | shop | ForSale | SYP | ar | 4 |
| 36 | e343b0e0-95ef-4d1d-b534-5745f0c5ef5e | 0305afbd-b699-479a-af97-dc5627e1d011 | As-Suwayda | warehouse | ForRent | EUR | ar | 3 |
| 35 | 4fef6b3b-a9cf-42ab-9cf5-1313219aac08 | cf274d27-f3d2-43b6-9e67-9db40627abf0 | Daraa | office | ForRentAndSale | USD | de | 10 |
| 37 | 024d1094-52bb-490d-b812-f36fbbfef65a | 2213e548-fe24-477a-bdb2-782437d9a056 | Damascus | commercial-land | ForSale | USD | ar | 4 |
| 38 | 407740b5-361f-44ee-9e0b-df3435d0a708 | 97b8fd22-0807-4d52-a6ed-0baadff9808c | Rural Damascus | industrial-land | ForSale | USD | en | 1 |
| 39 | 2da9dfb1-f6a5-45ca-bccb-ebcb42a72367 | 39acdbac-96f2-4d6a-b9ce-3f41f57ff3bb | Aleppo | hotel | ForSale | USD | ar | 4 |
| 40 | cf7feed6-616d-4ac4-be11-e17683b7de12 | a436e0e8-8a0f-4d9d-8705-7ba014633dfe | Homs | villa | ForSale | USD | ar | 6 |
| 41 | ac91e91a-854f-4651-a3f3-ffa91bdf5cf5 | 95fa1fee-5363-45e0-a5e4-f759a1b546d6 | Hama | apartment | ForRent | USD | ar | 3 |
| 42 | cce345a1-07bd-487b-9869-def09f26ec44 | af143b06-4812-4199-a643-c1779a6ac83e | Latakia | apartment | ForSale | USD | ar | 4 |
| 44 | 42e28230-20d6-448e-b26e-108077f64bc0 | 675229bf-75ea-41ff-9478-a5514cff0dbc | Deir ez-Zor | apartment | ForSale | USD | ar | 3 |
| 43 | bb2ddb67-6832-4d3d-b684-587c3353d0d0 | 071e97b4-2147-4c72-b91b-b446c241e79d | Tartus | apartment | ForRent | USD | en | 6 |
| 45 | 3a655ae3-930f-4772-aeaf-99439a5c3df0 | d5d87d53-126b-427d-8cd3-c47aa197d63d | Raqqa | apartment | ForRentAndSale | EUR | de | 4 |
| 46 | 76384dce-cf66-41f4-9ad0-a3fa9fe42c0d | 69f51d48-be81-48d2-82e3-9f2fa69168e6 | Idlib | apartment | ForRent | USD | ar | 0 |
| 47 | fad23e5e-9470-443a-b93e-c0648927b2b2 | f9f73702-0d70-40b4-a4b5-d8a1bc378f57 | Daraa | villa | ForSale | USD | ar | 4 |
| 49 | acae25f4-4e4c-4da7-89ee-4f3fead38c0d | 298d38a4-fcd1-4279-b055-8ea64b0e152f | Damascus | studio | ForRent | USD | ar | 1 |
| 48 | 08f972df-e85b-4274-84cd-a1d742a9b691 | bc0af166-9141-48d0-beda-bd57ffc10f13 | As-Suwayda | house | ForRentAndSale | USD | en | 6 |
| 50 | 13c7a62a-95b0-4fb7-bf4e-502ecfab54a3 | 1a7441c8-3b49-4183-a500-9b522f24b2e5 | Rural Damascus | hotel-apartment | ForSale | USD | ar | 4 |
| 52 | 798a6ec0-d03a-43d1-a66b-8de0740d333c | acfc56e6-e066-47ae-a562-ccd2b94e12c7 | Homs | agricultural-land | ForSale | USD | ar | 3 |
| 51 | 6372b441-b871-48a8-b811-fdd72c5ae462 | 2cb58c57-9cd1-49da-a750-ea1671d9b4d7 | Aleppo | residential-land | ForSale | SYP | ar | 6 |
| 53 | 3ef08890-5d05-4090-9a07-1515fbbc9a4a | 570d32cd-a970-4ec7-8c16-bcf7e843b648 | Hama | shop | ForRent | USD | en | 4 |
| 55 | 8fc20630-c611-47da-ac62-45ba44716cd7 | 8eb7ef2c-10e0-4683-b232-e8dbb79f1ebf | Tartus | office | ForRentAndSale | USD | de | 4 |
| 54 | bb12a1c0-2549-4508-a7c8-4ce82cd974e3 | 6ea7173c-91ac-4c82-926c-93f9793512bf | Latakia | shop | ForSale | EUR | ar | 10 |
| 56 | ad0e1b05-01fc-4d01-9294-0ccb00a11d93 | 48312011-81d8-49e6-91db-df80705d7c31 | Deir ez-Zor | warehouse | ForRent | USD | ar | 4 |
| 57 | 7719c3fe-a9f7-4da2-a2da-c3ba2baaa794 | 1678e9fe-7b84-41e9-8620-0699474459ac | Raqqa | commercial-land | ForSale | USD | ar | 1 |
| 58 | 2f45b1cf-55df-4fd7-ab0d-b04404b06372 | dd6fa73c-a4ec-4b8f-9e1c-9d95a4f8d92c | Idlib | industrial-land | ForSale | USD | en | 4 |
| 60 | 32d209fa-0484-40ff-bcf1-1e31a051ce3d | 70003f86-6435-45a8-b26a-cebe9959be65 | As-Suwayda | villa | ForSale | USD | ar | 3 |
| 59 | 767ffb55-612b-4d10-9494-fda6246bb44e | 0751676a-f7bf-4f32-9200-db0e9d144444 | Daraa | hotel | ForSale | USD | ar | 6 |
| 61 | b5c90623-31f2-4a71-8e3b-4198cb36157b | 5187b12b-0201-4f7b-bfd1-f98053c5f214 | Damascus | apartment | ForRent | USD | ar | 4 |
| 63 | 4f2a0cb0-ebc5-4936-ac9c-9122efc09f84 | 2cf4b8ef-6a45-43fe-8fce-0f0dbe077f59 | Aleppo | apartment | ForRent | EUR | en | 3 |
| 62 | d4760e47-0e4e-4ccf-b5dc-adb4d9761ccb | 6038bfe9-ee2f-466e-9373-5c02908b8c2d | Rural Damascus | apartment | ForSale | USD | ar | 10 |
| 64 | 27ec28d9-39e7-4430-bdfc-998bd2de0e0e | fd6e2af7-294b-49b5-96dc-0ecf260d15e6 | Homs | apartment | ForSale | USD | ar | 4 |
| 65 | e280f785-7bc9-4908-9596-b8fdf3496653 | 5ac29d60-8b24-411d-a226-a314086e0004 | Hama | apartment | ForRentAndSale | USD | de | 1 |
| 66 | 221de2f0-b626-427b-83b9-92ef51329a87 | 2b7d5965-5ae6-4e8d-a2e1-d8dca3b40376 | Latakia | apartment | ForRent | USD | ar | 4 |
| 68 | 75b9835b-cce2-4129-9e8c-da9da193c44d | 10c4605a-24f4-4699-9371-710c2bef5165 | Deir ez-Zor | house | ForRentAndSale | SYP | en | 3 |
| 67 | 23e5f24f-1662-4007-a751-e4f66a12727f | 3bb6d6eb-3d8e-497d-97d2-37b557586985 | Tartus | villa | ForSale | USD | ar | 6 |
| 69 | 0042ff30-5045-45e0-8f67-bcb2cd299837 | cad2b1fb-07d3-44ea-8d4a-349dc672d24f | Raqqa | studio | ForRent | USD | ar | 4 |
| 71 | 72453a64-e725-4448-9513-4e466012db61 | b48b0fe7-0c53-46f9-b5fc-8545ec9a4338 | Daraa | residential-land | ForSale | USD | ar | 3 |
| 70 | ab48fe19-bd6c-46c4-8818-9b86cf862b6a | ea514d8c-f3b0-4cf1-9ab7-c6a08f9eb639 | Idlib | hotel-apartment | ForSale | USD | ar | 6 |
| 73 | 69d7c60a-9d76-4b38-94ab-2cfb4237e846 | 7ff9e356-090b-4a58-b24c-66a262c721a9 | Damascus | shop | ForRent | USD | en | 0 |
| 72 | caccd262-650e-4cc1-baae-1b8b18ea66fa | 1a21050b-ecf2-461b-9f17-e403cf3943df | As-Suwayda | agricultural-land | ForSale | EUR | ar | 4 |
| 74 | 79ee5e1c-afe2-4c33-b80e-dd844b2e32fa | 08048729-088d-4289-8a3c-4bdea514e688 | Rural Damascus | shop | ForSale | USD | ar | 4 |
| 76 | c58f413c-4693-44d7-8a61-eae164b661ce | a1b50621-7f9a-4a8f-b857-c5176554061a | Homs | warehouse | ForRent | USD | ar | 1 |
| 75 | f80dddc2-45a9-44cb-af09-afa250bfb1a5 | bfdc8024-a246-42bb-881a-818d48016295 | Aleppo | office | ForRentAndSale | USD | de | 6 |
| 77 | cddf3890-0892-4be9-a48f-af77a554598e | 0decd71b-d82c-41cd-89f6-55e4f442df67 | Hama | commercial-land | ForSale | USD | ar | 4 |
| 78 | e2c019e6-b765-4797-a7f4-e302fff6a759 | 94662ffb-eea8-4868-883a-70e303778e4f | Latakia | industrial-land | ForSale | USD | en | 6 |
| 79 | 0dee8bcc-4336-4b16-b615-e7da54c311f4 | 19609315-b7b1-4037-b3b6-992d34c94e55 | Tartus | hotel | ForSale | USD | ar | 3 |
| 80 | 8c1a4032-7c49-4e84-bd7c-fd6b05cdcc4d | 6d968cf4-84f6-4f62-925d-38b63156b307 | Deir ez-Zor | villa | ForSale | USD | ar | 4 |
| 82 | ae22ee33-5f49-4cf1-9a62-dff34769f4f2 | ed07f754-5652-4b68-9863-5eefcae8c9ea | Idlib | apartment | ForSale | USD | ar | 4 |
| 83 | 977936d2-60d6-4d13-80ed-325061db15c8 | f4404e2b-e822-46ae-bd5f-a3032272f126 | Daraa | apartment | ForRent | USD | en | 4 |
| 81 | 21ebfdbd-1745-4fdb-801f-12cfdde8624c | d26e8acf-60ca-4906-8820-250c69f0d17c | Raqqa | apartment | ForRent | EUR | ar | 10 |
| 84 | 7509a468-221d-4adb-bcc1-c7045b86ff5f | dd7dcba9-a41e-4a03-b9ea-51468e705a80 | As-Suwayda | apartment | ForSale | USD | ar | 1 |
| 85 | e373e99b-340b-4375-a64b-ef633e00d244 | 247c2c0f-d555-4890-b3e8-a2378349822a | Damascus | apartment | ForRentAndSale | SYP | de | 4 |
| 86 | 38fea934-67a6-4f9b-9940-e714040d9ba7 | e2141952-42f4-451d-bdfd-85be5627b8de | Rural Damascus | apartment | ForRent | USD | ar | 6 |
| 87 | fb8cd189-ee77-4eea-8de8-a4c1ef55ad61 | cda4da09-d2fb-432f-b91b-d6bcd0cf1795 | Aleppo | villa | ForSale | USD | ar | 3 |
| 88 | f443539b-89be-4669-bada-3fd31f64d363 | aebd66d1-afdf-4196-b4b8-70b50ad0f85d | Homs | house | ForRentAndSale | USD | en | 4 |
| 89 | 72e45e11-8088-4643-bbe3-348a8372cab7 | 99d26cf9-d551-4150-bad8-31cb39c688d4 | Hama | studio | ForRent | USD | ar | 6 |
| 90 | 965f103b-f734-4907-a568-524fa576164f | 4d00dd6a-1953-4ea5-8ab8-ca80de4b5850 | Latakia | hotel-apartment | ForSale | EUR | ar | 3 |
| 91 | 93dba8fd-74b3-42b4-a8d1-394a81a2e245 | 31df5b90-264d-4d73-988f-8347ab4f2bcd | Tartus | residential-land | ForSale | USD | ar | 4 |
| 92 | 1ab00c36-312b-4f7f-a5f4-4155c4173569 | 81763acc-3daa-4d40-b189-e3d86a793d90 | Deir ez-Zor | agricultural-land | ForSale | USD | ar | 1 |
| 93 | 9cc5210d-be2b-40df-97f3-8a99374482f6 | 117ec77f-7b69-4b9d-b055-89e24a7f2b06 | Raqqa | shop | ForRent | USD | en | 4 |
| 94 | 544dd541-7ef0-4e48-9d1c-2160169e1c34 | 52e4b4c7-4af9-41f5-a3cf-7a4ca7988085 | Idlib | shop | ForSale | USD | ar | 6 |
| 95 | 4031307f-542b-45cb-af37-7c2dca97a081 | 4913ada0-6f45-4ffc-a792-f39e4fbd9354 | Daraa | office | ForRentAndSale | USD | de | 3 |
| 96 | 3591185e-329f-4d09-9a95-69146be34d36 | 6b8df52a-9b83-473b-9ee0-10666b06cb5c | As-Suwayda | warehouse | ForRent | USD | ar | 4 |
| 98 | 8d346a13-3619-454b-b133-084fd89ebe58 | 4e4fb7ac-e1d9-4e5b-927d-1662417211e9 | Rural Damascus | industrial-land | ForSale | USD | en | 3 |
| 97 | 88de3e70-5f37-4456-bc3b-a4d831b72aca | f10bd8be-c55c-4f3b-8a4e-2593823e1844 | Damascus | commercial-land | ForSale | USD | ar | 6 |
| 99 | e6ca0405-5349-4e98-a2c8-705849f8ce5d | d3cc2e1f-4c89-4d7a-8b08-b0d6ea97b2b4 | Aleppo | hotel | ForSale | EUR | ar | 4 |
| 100 | f86c8628-72de-45c7-85d6-61ece60e49a8 | 52dd2763-86e6-415c-9ebb-0db496b34e1b | Homs | villa | ForSale | USD | ar | 1 |
