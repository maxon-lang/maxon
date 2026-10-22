---
feature: ecdh
status: stable
keywords: [ecdh, EcdhP256, EcdhError, publicKey, sharedSecret, p-256, secp256r1, key agreement, elliptic curve, crypto]
category: crypto
---

# `EcdhP256` — elliptic-curve Diffie-Hellman over NIST P-256

## Documentation

| Call | Meaning |
|---|---|
| `EcdhP256.publicKey(privateKey)` | the 65-byte uncompressed SEC 1 point `04 ‖ x ‖ y` of `d · G`, for a 32-byte big-endian private key `d` |
| `EcdhP256.sharedSecret(privateKey, peerPublicKey: q)` | the 32-byte big-endian x-coordinate of `d · Q` (SP 800-56A ECC CDH, cofactor 1), leading zero bytes kept |

Both calls throw an `EcdhError`, and the private key is checked first:

1. a private key that is not exactly 32 bytes, or whose value is 0 or at least the group order `n`:
   `invalidPrivateKey`. The key is never reduced modulo `n`;
2. a peer key that is not 65 bytes starting with `04` (the identity `00`, compressed and hybrid points
   included), has a coordinate at least `p`, or is not on the curve: `invalidPublicKey`.

The scalar multiplication runs in time independent of the private key: a fixed 4-bit window over every bit
of the scalar, the table entry chosen by a masked scan of the whole table, and complete addition formulas.

The vectors are the canra `ecdh-p256` contract's: the 25 NIST CAVS `[P-256]` records, a Wycheproof
`ecdh_secp256r1_ecpoint_test` subset, computed edge keys and constructed refusals.

## Tests

<!-- test: ecdh.public-key -->
`EcdhP256.publicKey` against the canra ecdh-p256 vectors: NIST CAVS, computed edge keys and refused private keys. Vectors public-key-001 to public-key-err-007 (36).
```maxon
function digit(symbol Byte) returns Byte
	if symbol >= 0x61 'letter'
		return symbol - 0x57
	end 'letter'

	return symbol - 0x30
end 'digit'

function fromHex(hex String) returns ByteArray
	var bytes = ByteArray.create()
	var high = 0 as Byte
	var isHigh = true

	for symbol in hex.bytes() 'eachSymbol'
		if isHigh 'first'
			high = digit(symbol)
		end 'first' else 'second'
			bytes.push(high * 16 + digit(symbol))
		end 'second'

		isHigh = not isHigh
	end 'eachSymbol'

	return bytes
end 'fromHex'

function toHex(bytes ByteArray) returns String
	var text = ""

	for byte in bytes 'eachByte'
		text.append("{byte:02x}")
	end 'eachByte'

	return text
end 'toHex'

function errorName(failure EcdhError) returns String
	return match failure 'name'
		invalidPrivateKey gives "invalidPrivateKey"
		invalidPublicKey gives "invalidPublicKey"
	end 'name'
end 'errorName'

function report(id String, privateKeyHex String)
	if let point = try EcdhP256.publicKey(fromHex(privateKeyHex)) 'derived'
		print("{id} {toHex(point)}\n")
	end 'derived' else (failure) 'refused'
		print("{id} {errorName(failure)}\n")
	end 'refused'
end 'report'

function main() returns ExitCode
	report("public-key-001", privateKeyHex: "7d7dc5f71eb29ddaf80d6214632eeae03d9058af1fb6d22ed80badb62bc1a534")
	report("public-key-002", privateKeyHex: "38f65d6dce47676044d58ce5139582d568f64bb16098d179dbab07741dd5caf5")
	report("public-key-003", privateKeyHex: "1accfaf1b97712b85a6f54b148985a1bdc4c9bec0bd258cad4b3d603f49f32c8")
	report("public-key-004", privateKeyHex: "207c43a79bfee03db6f4b944f53d2fb76cc49ef1c9c4d34d51b6c65c4db6932d")
	report("public-key-005", privateKeyHex: "59137e38152350b195c9718d39673d519838055ad908dd4757152fd8255c09bf")
	report("public-key-006", privateKeyHex: "f5f8e0174610a661277979b58ce5c90fee6c9b3bb346a90a7196255e40b132ef")
	report("public-key-007", privateKeyHex: "3b589af7db03459c23068b64f63f28d3c3c6bc25b5bf76ac05f35482888b5190")
	report("public-key-008", privateKeyHex: "d8bf929a20ea7436b2461b541a11c80e61d826c0a4c9d322b31dd54e7f58b9c8")
	report("public-key-009", privateKeyHex: "0f9883ba0ef32ee75ded0d8bda39a5146a29f1f2507b3bd458dbea0b2bb05b4d")
	report("public-key-010", privateKeyHex: "2beedb04b05c6988f6a67500bb813faf2cae0d580c9253b6339e4a3337bb6c08")
	report("public-key-011", privateKeyHex: "77c15dcf44610e41696bab758943eff1409333e4d5a11bbe72c8f6c395e9f848")
	report("public-key-012", privateKeyHex: "42a83b985011d12303db1a800f2610f74aa71cdf19c67d54ce6c9ed951e9093e")
	report("public-key-013", privateKeyHex: "ceed35507b5c93ead5989119b9ba342cfe38e6e638ba6eea343a55475de2800b")
	report("public-key-014", privateKeyHex: "43e0e9d95af4dc36483cdd1968d2b7eeb8611fcce77f3a4e7d059ae43e509604")
	report("public-key-015", privateKeyHex: "b2f3600df3368ef8a0bb85ab22f41fc0e5f4fdd54be8167a5c3cd4b08db04903")
	report("public-key-016", privateKeyHex: "4002534307f8b62a9bf67ff641ddc60fef593b17c3341239e95bdb3e579bfdc8")
	report("public-key-017", privateKeyHex: "4dfa12defc60319021b681b3ff84a10a511958c850939ed45635934ba4979147")
	report("public-key-018", privateKeyHex: "1331f6d874a4ed3bc4a2c6e9c74331d3039796314beee3b7152fcdba5556304e")
	report("public-key-019", privateKeyHex: "dd5e9f70ae740073ca0204df60763fb6036c45709bf4a7bb4e671412fad65da3")
	report("public-key-020", privateKeyHex: "5ae026cfc060d55600717e55b8a12e116d1d0df34af831979057607c2d9c2f76")
	report("public-key-021", privateKeyHex: "b601ac425d5dbf9e1735c5e2d5bdb79ca98b3d5be4a2cfd6f2273f150e064d9d")
	report("public-key-022", privateKeyHex: "fefb1dda1845312b5fce6b81b2be205af2f3a274f5a212f66c0d9fc33d7ae535")
	report("public-key-023", privateKeyHex: "334ae0c4693d23935a7e8e043ebbde21e168a7cba3fa507c9be41d7681e049ce")
	report("public-key-024", privateKeyHex: "2c4bde40214fcc3bfc47d4cf434b629acbe9157f8fd0282540331de7942cf09d")
	report("public-key-025", privateKeyHex: "85a268f9d7772f990c36b42b0a331adc92b5941de0b862d5d89a347cbf8faab0")
	report("public-key-026", privateKeyHex: "0000000000000000000000000000000000000000000000000000000000000001")
	report("public-key-027", privateKeyHex: "0000000000000000000000000000000000000000000000000000000000000002")
	report("public-key-028", privateKeyHex: "ffffffff00000000ffffffffffffffffbce6faada7179e84f3b9cac2fc632550")
	report("public-key-029", privateKeyHex: "ffffffff00000000ffffffffffffffffbce6faada7179e84f3b9cac2fc63254f")
	report("public-key-err-001", privateKeyHex: "0000000000000000000000000000000000000000000000000000000000000000")
	report("public-key-err-002", privateKeyHex: "ffffffff00000000ffffffffffffffffbce6faada7179e84f3b9cac2fc632551")
	report("public-key-err-003", privateKeyHex: "ffffffff00000000ffffffffffffffffbce6faada7179e84f3b9cac2fc632552")
	report("public-key-err-004", privateKeyHex: "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff")
	report("public-key-err-005", privateKeyHex: "00000000000000000000000000000000000000000000000000000000000001")
	report("public-key-err-006", privateKeyHex: "000000000000000000000000000000000000000000000000000000000000000001")
	report("public-key-err-007", privateKeyHex: "")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
public-key-001 04ead218590119e8876b29146ff89ca61770c4edbbf97d38ce385ed281d8a6b23028af61281fd35e2fa7002523acc85a429cb06ee6648325389f59edfce1405141
public-key-002 04119f2f047902782ab0c9e27a54aff5eb9b964829ca99c06b02ddba95b0a3f6d08f52b726664cac366fc98ac7a012b2682cbd962e5acb544671d41b9445704d1d
public-key-003 04d9f2b79c172845bfdb560bbb01447ca5ecc0470a09513b6126902c6b4f8d1051f815ef5ec32128d3487834764678702e64e164ff7315185e23aff5facd96d7bc
public-key-004 0424277c33f450462dcb3d4801d57b9ced05188f16c28eda873258048cd1607e0dc4789753e2b1f63b32ff014ec42cd6a69fac81dfe6d0d6fd4af372ae27c46f88
public-key-005 04a8c5fdce8b62c5ada598f141adb3b26cf254c280b2857a63d2ad783a73115f6b806e1aafec4af80a0d786b3de45375b517a7e5b51ffb2c356537c9e6ef227d4a
public-key-006 047b861dcd2844a5a8363f6b8ef8d493640f55879217189d80326aad9480dfc149c4675b45eeb306405f6c33c38bc69eb2bdec9b75ad5af4706aab84543b9cc63a
public-key-007 049fb38e2d58ea1baf7622e96720101cae3cde4ba6c1e9fa26d9b1de0899102863d5561b900406edf50802dd7d73e89395f8aed72fba0e1d1b61fe1d22302260f0
public-key-008 0420f07631e4a6512a89ad487c4e9d63039e579cb0d7a556cb9e661cd59c1e7fa46de91846b3eee8a5ec09c2ab1f41e21bd83620ccdd1bdce3ab7ea6e02dd274f5
public-key-009 04abb61b423be5d6c26e21c605832c9142dc1dfe5a5fff28726737936e6fbf516d733d2513ef58beab202090586fac91bf0fee31e80ab33473ab23a2d89e58fad6
public-key-010 043d63e429cb5fa895a9247129bf4e48e89f35d7b11de8158efeb3e106a2a873950cae9e477ef41e7c8c1064379bb7b554ddcbcae79f9814281f1e50f0403c61f3
public-key-011 04ad5d13c3db508ddcd38457e5991434a251bed49cf5ddcb59cdee73865f138c9f62cec1e70588aa4fdfc7b9a09daa678081c04e1208b9d662b8a2214bf8e81a21
public-key-012 04ab48caa61ea35f13f8ed07ffa6a13e8db224dfecfae1a7df8b1bb6ebaf0cb97d1274530ca2c385a3218bddfbcbf0b4024c9badd5243bff834ebff24a8618dccb
public-key-013 049a8cd9bd72e71752df91440f77c547509a84df98114e7de4f26cdb39234a625dd07cfc84c8e144fab2839f5189bb1d7c88631d579bbc58012ed9a2327da52f62
public-key-014 04f989cf8ee956a82e7ebd9881cdbfb2fd946189b08db53559bc8cfdd48071eb145eff28f1a18a616b04b7d337868679f6dd84f9a7b3d7b6f8af276c19611a541d
public-key-015 0469c627625b36a429c398b45c38677cb35d8beb1cf78a571e40e99fe4eac1cd4e81690112b0a88f20f7136b28d7d47e5fbc2ada3c8edd87589bc19ec9590637bd
public-key-016 045fe964671315a18aa68a2a6e3dd1fde7e23b8ce7181471cfac43c99e1ae80262d5827be282e62c84de531b963884ba832db5d6b2c3a256f0e604fe7e6b8a7f72
public-key-017 04c9b2b8496f1440bd4a2d1e52752fd372835b364885e154a7dac49295f281ec7cfbe6b926a8a4de26ccc83b802b1212400754be25d9f3eeaf008b09870ae76321
public-key-018 0459e1e101521046ad9cf1d082e9d2ec7dd22530cce064991f1e55c5bcf5fcb591482f4f673176c8fdaa0bb6e59b15a3e47454e3a04297d3863c9338d98add1f37
public-key-019 0430b9db2e2e977bcdc98cb87dd736cbd8e78552121925cf16e1933657c2fb23146a45028800b81291bce5c2e1fed7ded650620ebbe6050c6f3a7f0dfb4673ab5c
public-key-020 0446c9ebd1a4a3c8c0b6d572b5dcfba12467603208a9cb5d2acfbb733c40cf639146c913a27d044185d38b467ace011e04d4d9bbbb8cb9ae25fa92aaf15a595e86
public-key-021 047c9e950841d26c8dde8994398b8f5d475a022bc63de7773fcf8d552e01f1ba0acc42b9885c9b3bee0f8d8c57d3a8f6355016c019c4062fa22cff2f209b5cc2e1
public-key-022 0438b54db85500cb20c61056edd3d88b6a9dc26780a047f213a6e1b900f76596eb6387e4e5781571e4eb8ae62991a33b5dc33301c5bc7e125d53794a39160d8fd0
public-key-023 043f2bf1589abf3047bf3e54ac9a95379bff95f8f55405f64eca36a7eebe8ffca75212a94e66c5ae9a8991872f66a72723d80ec5b2e925745c456f5371943b3a06
public-key-024 0429c0807f10cbc42fb45c9989da50681eead716daa7b9e91fd32e062f5eb92ca0ff1d6d1955d7376b2da24fe1163a271659136341bc2eb1195fc706dc62e7f34d
public-key-025 049cf4b98581ca1779453cc816ff28b4100af56cf1bf2e5bc312d83b6b1b21d3337a5504fcac5231a0d12d658218284868229c844a04a3450d6c7381abe080bf3b
public-key-026 046b17d1f2e12c4247f8bce6e563a440f277037d812deb33a0f4a13945d898c2964fe342e2fe1a7f9b8ee7eb4a7c0f9e162bce33576b315ececbb6406837bf51f5
public-key-027 047cf27b188d034f7e8a52380304b51ac3c08969e277f21b35a60b48fc4766997807775510db8ed040293d9ac69f7430dbba7dade63ce982299e04b79d227873d1
public-key-028 046b17d1f2e12c4247f8bce6e563a440f277037d812deb33a0f4a13945d898c296b01cbd1c01e58065711814b583f061e9d431cca994cea1313449bf97c840ae0a
public-key-029 047cf27b188d034f7e8a52380304b51ac3c08969e277f21b35a60b48fc47669978f888aaee24712fc0d6c26539608bcf244582521ac3167dd661fb4862dd878c2e
public-key-err-001 invalidPrivateKey
public-key-err-002 invalidPrivateKey
public-key-err-003 invalidPrivateKey
public-key-err-004 invalidPrivateKey
public-key-err-005 invalidPrivateKey
public-key-err-006 invalidPrivateKey
public-key-err-007 invalidPrivateKey
```

<!-- test: ecdh.shared-secret-1 -->
`EcdhP256.sharedSecret` against the canra ecdh-p256 vectors: NIST CAVS, Wycheproof and constructed refusals. Vectors shared-secret-001 to shared-secret-073 (73).
```maxon
function digit(symbol Byte) returns Byte
	if symbol >= 0x61 'letter'
		return symbol - 0x57
	end 'letter'

	return symbol - 0x30
end 'digit'

function fromHex(hex String) returns ByteArray
	var bytes = ByteArray.create()
	var high = 0 as Byte
	var isHigh = true

	for symbol in hex.bytes() 'eachSymbol'
		if isHigh 'first'
			high = digit(symbol)
		end 'first' else 'second'
			bytes.push(high * 16 + digit(symbol))
		end 'second'

		isHigh = not isHigh
	end 'eachSymbol'

	return bytes
end 'fromHex'

function toHex(bytes ByteArray) returns String
	var text = ""

	for byte in bytes 'eachByte'
		text.append("{byte:02x}")
	end 'eachByte'

	return text
end 'toHex'

function errorName(failure EcdhError) returns String
	return match failure 'name'
		invalidPrivateKey gives "invalidPrivateKey"
		invalidPublicKey gives "invalidPublicKey"
	end 'name'
end 'errorName'

function report(id String, privateKeyHex String, peerPublicKeyHex String)
	if let secret = try EcdhP256.sharedSecret(fromHex(privateKeyHex), peerPublicKey: fromHex(peerPublicKeyHex)) 'agreed'
		print("{id} {toHex(secret)}\n")
	end 'agreed' else (failure) 'refused'
		print("{id} {errorName(failure)}\n")
	end 'refused'
end 'report'

function main() returns ExitCode
	report("shared-secret-001", privateKeyHex: "7d7dc5f71eb29ddaf80d6214632eeae03d9058af1fb6d22ed80badb62bc1a534", peerPublicKeyHex: "04700c48f77f56584c5cc632ca65640db91b6bacce3a4df6b42ce7cc838833d287db71e509e3fd9b060ddb20ba5c51dcc5948d46fbf640dfe0441782cab85fa4ac")
	report("shared-secret-002", privateKeyHex: "38f65d6dce47676044d58ce5139582d568f64bb16098d179dbab07741dd5caf5", peerPublicKeyHex: "04809f04289c64348c01515eb03d5ce7ac1a8cb9498f5caa50197e58d43a86a7aeb29d84e811197f25eba8f5194092cb6ff440e26d4421011372461f579271cda3")
	report("shared-secret-003", privateKeyHex: "1accfaf1b97712b85a6f54b148985a1bdc4c9bec0bd258cad4b3d603f49f32c8", peerPublicKeyHex: "04a2339c12d4a03c33546de533268b4ad667debf458b464d77443636440ee7fec3ef48a3ab26e20220bcda2c1851076839dae88eae962869a497bf73cb66faf536")
	report("shared-secret-004", privateKeyHex: "207c43a79bfee03db6f4b944f53d2fb76cc49ef1c9c4d34d51b6c65c4db6932d", peerPublicKeyHex: "04df3989b9fa55495719b3cf46dccd28b5153f7808191dd518eff0c3cff2b705ed422294ff46003429d739a33206c8752552c8ba54a270defc06e221e0feaf6ac4")
	report("shared-secret-005", privateKeyHex: "59137e38152350b195c9718d39673d519838055ad908dd4757152fd8255c09bf", peerPublicKeyHex: "0441192d2813e79561e6a1d6f53c8bc1a433a199c835e141b05a74a97b0faeb9221af98cc45e98a7e041b01cf35f462b7562281351c8ebf3ffa02e33a0722a1328")
	report("shared-secret-006", privateKeyHex: "f5f8e0174610a661277979b58ce5c90fee6c9b3bb346a90a7196255e40b132ef", peerPublicKeyHex: "0433e82092a0f1fb38f5649d5867fba28b503172b7035574bf8e5b7100a3052792f2cf6b601e0a05945e335550bf648d782f46186c772c0f20d3cd0d6b8ca14b2f")
	report("shared-secret-007", privateKeyHex: "3b589af7db03459c23068b64f63f28d3c3c6bc25b5bf76ac05f35482888b5190", peerPublicKeyHex: "046a9e0c3f916e4e315c91147be571686d90464e8bf981d34a90b6353bca6eeba740f9bead39c2f2bcc2602f75b8a73ec7bdffcbcead159d0174c6c4d3c5357f05")
	report("shared-secret-008", privateKeyHex: "d8bf929a20ea7436b2461b541a11c80e61d826c0a4c9d322b31dd54e7f58b9c8", peerPublicKeyHex: "04a9c0acade55c2a73ead1a86fb0a9713223c82475791cd0e210b046412ce224bbf6de0afa20e93e078467c053d241903edad734c6b403ba758c2b5ff04c9d4229")
	report("shared-secret-009", privateKeyHex: "0f9883ba0ef32ee75ded0d8bda39a5146a29f1f2507b3bd458dbea0b2bb05b4d", peerPublicKeyHex: "0494e94f16a98255fff2b9ac0c9598aac35487b3232d3231bd93b7db7df36f9eb9d8049a43579cfa90b8093a94416cbefbf93386f15b3f6e190b6e3455fedfe69a")
	report("shared-secret-010", privateKeyHex: "2beedb04b05c6988f6a67500bb813faf2cae0d580c9253b6339e4a3337bb6c08", peerPublicKeyHex: "04e099bf2a4d557460b5544430bbf6da11004d127cb5d67f64ab07c94fcdf5274fd9c50dbe70d714edb5e221f4e020610eeb6270517e688ca64fb0e98c7ef8c1c5")
	report("shared-secret-011", privateKeyHex: "77c15dcf44610e41696bab758943eff1409333e4d5a11bbe72c8f6c395e9f848", peerPublicKeyHex: "04f75a5fe56bda34f3c1396296626ef012dc07e4825838778a645c8248cff0165833bbdf1b1772d8059df568b061f3f1122f28a8d819167c97be448e3dc3fb0c3c")
	report("shared-secret-012", privateKeyHex: "42a83b985011d12303db1a800f2610f74aa71cdf19c67d54ce6c9ed951e9093e", peerPublicKeyHex: "042db4540d50230756158abf61d9835712b6486c74312183ccefcaef2797b7674d62f57f314e3f3495dc4e099012f5e0ba71770f9660a1eada54104cdfde77243e")
	report("shared-secret-013", privateKeyHex: "ceed35507b5c93ead5989119b9ba342cfe38e6e638ba6eea343a55475de2800b", peerPublicKeyHex: "04cd94fc9497e8990750309e9a8534fd114b0a6e54da89c4796101897041d14ecbc3def4b5fe04faee0a11932229fff563637bfdee0e79c6deeaf449f85401c5c4")
	report("shared-secret-014", privateKeyHex: "43e0e9d95af4dc36483cdd1968d2b7eeb8611fcce77f3a4e7d059ae43e509604", peerPublicKeyHex: "0415b9e467af4d290c417402e040426fe4cf236bae72baa392ed89780dfccdb471cdf4e9170fb904302b8fd93a820ba8cc7ed4efd3a6f2d6b05b80b2ff2aee4e77")
	report("shared-secret-015", privateKeyHex: "b2f3600df3368ef8a0bb85ab22f41fc0e5f4fdd54be8167a5c3cd4b08db04903", peerPublicKeyHex: "0449c503ba6c4fa605182e186b5e81113f075bc11dcfd51c932fb21e951eee2fa18af706ff0922d87b3f0c5e4e31d8b259aeb260a9269643ed520a13bb25da5924")
	report("shared-secret-016", privateKeyHex: "4002534307f8b62a9bf67ff641ddc60fef593b17c3341239e95bdb3e579bfdc8", peerPublicKeyHex: "0419b38de39fdd2f70f7091631a4f75d1993740ba9429162c2a45312401636b29c09aed7232b28e060941741b6828bcdfa2bc49cc844f3773611504f82a390a5ae")
	report("shared-secret-017", privateKeyHex: "4dfa12defc60319021b681b3ff84a10a511958c850939ed45635934ba4979147", peerPublicKeyHex: "042c91c61f33adfe9311c942fdbff6ba47020feff416b7bb63cec13faf9b0999546cab31b06419e5221fca014fb84ec870622a1b12bab5ae43682aa7ea73ea08d0")
	report("shared-secret-018", privateKeyHex: "1331f6d874a4ed3bc4a2c6e9c74331d3039796314beee3b7152fcdba5556304e", peerPublicKeyHex: "04a28a2edf58025668f724aaf83a50956b7ac1cfbbff79b08c3bf87dfd2828d767dfa7bfffd4c766b86abeaf5c99b6e50cb9ccc9d9d00b7ffc7804b0491b67bc03")
	report("shared-secret-019", privateKeyHex: "dd5e9f70ae740073ca0204df60763fb6036c45709bf4a7bb4e671412fad65da3", peerPublicKeyHex: "04a2ef857a081f9d6eb206a81c4cf78a802bdf598ae380c8886ecd85fdc1ed7644563c4c20419f07bc17d0539fade1855e34839515b892c0f5d26561f97fa04d1a")
	report("shared-secret-020", privateKeyHex: "5ae026cfc060d55600717e55b8a12e116d1d0df34af831979057607c2d9c2f76", peerPublicKeyHex: "04ccd8a2d86bc92f2e01bce4d6922cf7fe1626aed044685e95e2eebd464505f01fe9ddd583a9635a667777d5b8a8f31b0f79eba12c75023410b54b8567dddc0f38")
	report("shared-secret-021", privateKeyHex: "b601ac425d5dbf9e1735c5e2d5bdb79ca98b3d5be4a2cfd6f2273f150e064d9d", peerPublicKeyHex: "04c188ffc8947f7301fb7b53e36746097c2134bf9cc981ba74b4e9c4361f595e4ebf7d2f2056e72421ef393f0c0f2b0e00130e3cac4abbcc00286168e85ec55051")
	report("shared-secret-022", privateKeyHex: "fefb1dda1845312b5fce6b81b2be205af2f3a274f5a212f66c0d9fc33d7ae535", peerPublicKeyHex: "04317e1020ff53fccef18bf47bb7f2dd7707fb7b7a7578e04f35b3beed222a0eb609420ce5a19d77c6fe1ee587e6a49fbaf8f280e8df033d75403302e5a27db2ae")
	report("shared-secret-023", privateKeyHex: "334ae0c4693d23935a7e8e043ebbde21e168a7cba3fa507c9be41d7681e049ce", peerPublicKeyHex: "0445fb02b2ceb9d7c79d9c2fa93e9c7967c2fa4df5789f9640b24264b1e524fcb15c6e8ecf1f7d3023893b7b1ca1e4d178972ee2a230757ddc564ffe37f5c5a321")
	report("shared-secret-024", privateKeyHex: "2c4bde40214fcc3bfc47d4cf434b629acbe9157f8fd0282540331de7942cf09d", peerPublicKeyHex: "04a19ef7bff98ada781842fbfc51a47aff39b5935a1c7d9625c8d323d511c92de6e9c184df75c955e02e02e400ffe45f78f339e1afe6d056fb3245f4700ce606ef")
	report("shared-secret-025", privateKeyHex: "85a268f9d7772f990c36b42b0a331adc92b5941de0b862d5d89a347cbf8faab0", peerPublicKeyHex: "04356c5a444c049a52fee0adeb7e5d82ae5aa83030bfff31bbf8ce2096cf161c4b57d128de8b2a57a094d1a001e572173f96e8866ae352bf29cddaf92fc85b2f92")
	report("shared-secret-026", privateKeyHex: "0612465c89a023ab17855b0a6bcebfd3febb53aef84138647b5352e02c10c346", peerPublicKeyHex: "0462d5bd3372af75fe85a040715d0f502428e07046868b0bfdfa61d731afe44f26ac333a93a9e70a81cd5a95b5bf8d13990eb741c8c38872b4a07d275a014e30cf")
	report("shared-secret-027", privateKeyHex: "0a0d622a47e48f6bc1038ace438c6f528aa00ad2bd1da5f13ee46bf5f633d71a", peerPublicKeyHex: "0458fd4168a87795603e2b04390285bdca6e57de6027fe211dd9d25e2212d29e62080d36bd224d7405509295eed02a17150e03b314f96da37445b0d1d29377d12c")
	report("shared-secret-028", privateKeyHex: "0a0d622a47e48f6bc1038ace438c6f528aa00ad2bd1da5f13ee46bf5f633d71a", peerPublicKeyHex: "04a1ecc24bf0d0053d23f5fd80ddf1735a1925039dc1176c581a7e795163c8b9ba2cb5a4e4d5109f4527575e3137b83d79a9bcb3faeff90d2aca2bed71bb523e7e")
	report("shared-secret-029", privateKeyHex: "0a0d622a47e48f6bc1038ace438c6f528aa00ad2bd1da5f13ee46bf5f633d71a", peerPublicKeyHex: "041b0e7437c33d379929430d3ec10df59bed7fe2a1d950c5791e1e9ddeef1f4d70fbdb0e3bbce63a27f27838c685207f2ccaf689d25eb622744db1168ac92619e8")
	report("shared-secret-030", privateKeyHex: "0a0d622a47e48f6bc1038ace438c6f528aa00ad2bd1da5f13ee46bf5f633d71a", peerPublicKeyHex: "0408d6148d0244d243b3d0d1777de6375fa7ebeab477f19915d05994db04df219727737d4f8ce0a7f390becce92b2bcd5c054f18ecb58e5dd59baf88b4ed6abea8")
	report("shared-secret-031", privateKeyHex: "0a0d622a47e48f6bc1038ace438c6f528aa00ad2bd1da5f13ee46bf5f633d71a", peerPublicKeyHex: "042424f9d7ba0a89ce3c7c1e8f15dfd83004d86680967a82cbf9bb6b11dae5fd72e05c3687187b1cf28438a17a7469fa3b094b7d6f36cccc3492c0b0a61fab2a38")
	report("shared-secret-032", privateKeyHex: "0a0d622a47e48f6bc1038ace438c6f528aa00ad2bd1da5f13ee46bf5f633d71a", peerPublicKeyHex: "044c9695b7668434fc85769acb10f0edcf87a96b7d5dc347b46bb304b0b1d3267fcf5d993bff2a8c774808231a34f41b33667d8ebeafd00689fa3a9bfa05a40c1c")
	report("shared-secret-033", privateKeyHex: "0a0d622a47e48f6bc1038ace438c6f528aa00ad2bd1da5f13ee46bf5f633d71a", peerPublicKeyHex: "04a492fe4b4908b6d675d687551f99c617e4e5c97aa266958953129eb381f0153b111b95c94fa1d1ecd1d41d2785c1db5195875ae98051732a59ba7720f9089af6")
	report("shared-secret-034", privateKeyHex: "0a0d622a47e48f6bc1038ace438c6f528aa00ad2bd1da5f13ee46bf5f633d71a", peerPublicKeyHex: "04d5c96efd1907fd48de2ad715acf82eae5c6690fe3efe16a78d61c68d3bfd10df03eac816b9e7b776192a3f5075887c0e225617505833ca997cda32fd0f673c5e")
	report("shared-secret-035", privateKeyHex: "0a0d622a47e48f6bc1038ace438c6f528aa00ad2bd1da5f13ee46bf5f633d71a", peerPublicKeyHex: "04c74d546f2fcc6dd392f85e5be167e358de908756b0c0bb01cb69d864ca083e1c93f959eece6e10ee11bd3934207d65ae28af68b092585a1509260eceb39b92ef")
	report("shared-secret-036", privateKeyHex: "0a0d622a47e48f6bc1038ace438c6f528aa00ad2bd1da5f13ee46bf5f633d71a", peerPublicKeyHex: "0434fc9f1e7a094cd29598d1841fa9613dbe82313d633a51d63fb6eff074cc9b9a4ecfd9f258c5c4d4210b49751213a24c596982bd1d54e0445443f21ef15492a5")
	report("shared-secret-037", privateKeyHex: "0a0d622a47e48f6bc1038ace438c6f528aa00ad2bd1da5f13ee46bf5f633d71a", peerPublicKeyHex: "04f475f503a770df72c45aedfe42c008f59aa57e72b232f26600bdd0353957cb20bdb8f6405b4918050a3549f44c07a8eba820cdce4ece699888c638df66f54f7c")
	report("shared-secret-038", privateKeyHex: "0a0d622a47e48f6bc1038ace438c6f528aa00ad2bd1da5f13ee46bf5f633d71a", peerPublicKeyHex: "04949304889050834006ecf11eba0e73f0dd2a5e77a5ea642a34a3f9989eda2d57384509cd42763c87cf460d1ad75664402986bb8cca0acdfc3685d2c9c7d32933")
	report("shared-secret-039", privateKeyHex: "0a0d622a47e48f6bc1038ace438c6f528aa00ad2bd1da5f13ee46bf5f633d71a", peerPublicKeyHex: "04e25ead8342888d1fbd2bd211e9db5bc761da9cfa1e805078b53606df9e5444403d3421fa70f940dba104d842034d2efec9332161ccfb654a8e9f2ec42fc1a837")
	report("shared-secret-040", privateKeyHex: "0a0d622a47e48f6bc1038ace438c6f528aa00ad2bd1da5f13ee46bf5f633d71a", peerPublicKeyHex: "04cb32ab951d7581e7602c89320d91d7043e9dc56a10348d586a08d1870753152bf96bd53e7cfc2434dc61a09f8f4853e4c6dd49e786194ec0a988d0e8d29378b8")
	report("shared-secret-041", privateKeyHex: "0a0d622a47e48f6bc1038ace438c6f528aa00ad2bd1da5f13ee46bf5f633d71a", peerPublicKeyHex: "040b9d56422ed9c2082dd9d46d231fd0141f92e79f5a12da4c77c488c87363f944e6e3c2ec9779615f242bc30d1c28c1f984b8c252243f5f1eaa3d855e1cc2d8a6")
	report("shared-secret-042", privateKeyHex: "0a0d622a47e48f6bc1038ace438c6f528aa00ad2bd1da5f13ee46bf5f633d71a", peerPublicKeyHex: "040507fabb16755f4885ae45b3d497822388f4e6a531de512c200480b4e56b892ab70746c1d8a20637eff17948e557a2b4da2562ba6b62991b426b7b3787a19693")
	report("shared-secret-043", privateKeyHex: "0a0d622a47e48f6bc1038ace438c6f528aa00ad2bd1da5f13ee46bf5f633d71a", peerPublicKeyHex: "043b18caef2cd982c3b58f258cb76daa029383f42c8f3f083493f42c915935243a260830b7d25b66378c5d25fb0cf23d32527a2cdb7f7d7e6c89ebea8277738ed8")
	report("shared-secret-044", privateKeyHex: "0a0d622a47e48f6bc1038ace438c6f528aa00ad2bd1da5f13ee46bf5f633d71a", peerPublicKeyHex: "04123b1921231bee315e1ea66cb95318d6392f85695bbec03c334740ce7add768efec9a108770ff0eb448eee9f06367416d74b22a2f38157bb00d8c0be44ebf152")
	report("shared-secret-045", privateKeyHex: "0a0d622a47e48f6bc1038ace438c6f528aa00ad2bd1da5f13ee46bf5f633d71a", peerPublicKeyHex: "04924fb33985c8a687fc04c9dd05e531ca0e0223aa58d58351e922ef482043d30cf504745e769b6dcbefe404da37f717b3109d2af23450fcfe2f075c2dabbe7194")
	report("shared-secret-046", privateKeyHex: "0a0d622a47e48f6bc1038ace438c6f528aa00ad2bd1da5f13ee46bf5f633d71a", peerPublicKeyHex: "0422495c6884937a1bc05871e9ee1a2c43f8f14db19ee60bd2d89c4293042f5a9452cea27e6fe574a00e5f15fe11aa54f54ebd8bde3aa41c80bf9d5f7b1dc6efec")
	report("shared-secret-047", privateKeyHex: "0a0d622a47e48f6bc1038ace438c6f528aa00ad2bd1da5f13ee46bf5f633d71a", peerPublicKeyHex: "04f3cb6754b7e2a86d064dfb9f903185aaa4c92b481c2c1a1ff276303bbc4183e49c318599b0984c3563df339311fe143a7d921ee75b755a52c6f804f897b809f7")
	report("shared-secret-048", privateKeyHex: "0a0d622a47e48f6bc1038ace438c6f528aa00ad2bd1da5f13ee46bf5f633d71a", peerPublicKeyHex: "04a8d93bebbaf25fa9a1f98c44b54dbf28634a5fd08402dba5d9e4873f8123da1db972d91a0eabadb630b271e6551b757d969301beddd11f82decdfbe4f9657f50")
	report("shared-secret-049", privateKeyHex: "0a0d622a47e48f6bc1038ace438c6f528aa00ad2bd1da5f13ee46bf5f633d71a", peerPublicKeyHex: "048d0086e678a0fe7b4ad7bd4f94a38267b9f9f1d2b82452db4cfb335595c26c55f78476a00a0153941da0b0b81c834682edc9fcc8bc21a7b5bacb460dbb566c19")
	report("shared-secret-050", privateKeyHex: "0a0d622a47e48f6bc1038ace438c6f528aa00ad2bd1da5f13ee46bf5f633d71a", peerPublicKeyHex: "0402a96834fe3b54d440003521f2355aea48d0bbc576473fecc6975100c589865263255764891ffc108cfe754af52a4362953955ddfadcf752637c11edeaa6a807")
	report("shared-secret-051", privateKeyHex: "0a0d622a47e48f6bc1038ace438c6f528aa00ad2bd1da5f13ee46bf5f633d71a", peerPublicKeyHex: "048721294f9eb3ba69a13ae7d933fa51c48393fe1b9b9b17a08cecff2ee6ad434d2c23cd11d26694c472dda8cea4133bbc68e1f234bc7e8bffcea9446091855491")
	report("shared-secret-052", privateKeyHex: "0a0d622a47e48f6bc1038ace438c6f528aa00ad2bd1da5f13ee46bf5f633d71a", peerPublicKeyHex: "043e99ec3c58ac3898936e4a61182b198823c9992209f67bf5291aa354fceb2f1312952518636add666320c8b7fa631541d0ca50796dbff0c1e72f94718776bd60")
	report("shared-secret-053", privateKeyHex: "0a0d622a47e48f6bc1038ace438c6f528aa00ad2bd1da5f13ee46bf5f633d71a", peerPublicKeyHex: "04e9484e58f3331b66ffed6d90cb1c78065fa28cfba5c7dd4352013d3252ee4277bd7503b045a38b4b247b32c59593580f39e6abfa376c3dca20cf7f9cfb659e13")
	report("shared-secret-054", privateKeyHex: "0a0d622a47e48f6bc1038ace438c6f528aa00ad2bd1da5f13ee46bf5f633d71a", peerPublicKeyHex: "04eb6eb532fe4af2a5a275ab4e9f728ae64e9dbc08e9359091d75e5334a6e08db64c146339c05d1d5782ebeeb86d3eb5c63e9f99f17187c039666c5237b736e9cd")
	report("shared-secret-055", privateKeyHex: "0a0d622a47e48f6bc1038ace438c6f528aa00ad2bd1da5f13ee46bf5f633d71a", peerPublicKeyHex: "048e7b50f7d8c44d5d3496c43141a502f4a43f153d03ad43eda8e39597f1d477b8647f3da67969b7f989ff4addc393515af40c82085ce1f2ee195412c6f583774f")
	report("shared-secret-056", privateKeyHex: "0a0d622a47e48f6bc1038ace438c6f528aa00ad2bd1da5f13ee46bf5f633d71a", peerPublicKeyHex: "0440d4f4f6d9a41ab6ea90d46610b924ab15ed4f259fd09ddd41a707782fa9e203a5080a7e118a50c4b3d2c43dcbbf35c39e276a0a50b33a9ffbe7c9095055fc25")
	report("shared-secret-057", privateKeyHex: "0a0d622a47e48f6bc1038ace438c6f528aa00ad2bd1da5f13ee46bf5f633d71a", peerPublicKeyHex: "04a386ace573f87558a68ead2a20088e3fe928bdae9e109446f93a078c15741f0421261e6db2bf12106e4c6bf85b9581b4c0302a526222f90abc5a549206b11011")
	report("shared-secret-058", privateKeyHex: "0a0d622a47e48f6bc1038ace438c6f528aa00ad2bd1da5f13ee46bf5f633d71a", peerPublicKeyHex: "04739b5c2189729b577ba1a467a1a0851c556b860f6b2016bcd50beb22ee9c0d7961ea43cf1ea7aad1a2360b0f37a74ca532dc8d1af26cdf89a87d93dcaa0c9564")
	report("shared-secret-059", privateKeyHex: "0a0d622a47e48f6bc1038ace438c6f528aa00ad2bd1da5f13ee46bf5f633d71a", peerPublicKeyHex: "046c03f055f710aea89341f0ef7a1c6d5c45a50923ce8adb33c2520f7881547748691f4dafcf6d5df8d9e6afda66ca81718bea8171abc87036a9c370385b60d969")
	report("shared-secret-060", privateKeyHex: "0a0d622a47e48f6bc1038ace438c6f528aa00ad2bd1da5f13ee46bf5f633d71a", peerPublicKeyHex: "048a3f60cfec64745bf625696f2fe4b6e4ce8208954f4fc4da21f87f9f7a2c3275b54d90851b39707586dc6cceccc5e50a1e4a955463107d1d87c8252df2110d6d")
	report("shared-secret-061", privateKeyHex: "0a0d622a47e48f6bc1038ace438c6f528aa00ad2bd1da5f13ee46bf5f633d71a", peerPublicKeyHex: "04c827fb930fd51d926086191b502af83abb5f717debc8de29897a3934b2571ca05990c0597b0b7a2e42febd56b13235d1d408d76ed2c93b3facf514d902f6910a")
	report("shared-secret-062", privateKeyHex: "0a0d622a47e48f6bc1038ace438c6f528aa00ad2bd1da5f13ee46bf5f633d71a", peerPublicKeyHex: "04fc85bfee056478a1717582accd11ed49009891cbda5fe40b9f4c1742e127db2c97033f5f6405acfa57553f1e375a13e8d6e3a1151958c709be508e0bac7804d6")
	report("shared-secret-063", privateKeyHex: "0a0d622a47e48f6bc1038ace438c6f528aa00ad2bd1da5f13ee46bf5f633d71a", peerPublicKeyHex: "0438bf171cdbd4e2e7128528db43d80655b789e69e6ba1d36b62e4e59d8ecf4c51bbe3244e4fbf79f24512f30c4a605314749e2f20d858ec961060a2cab467013f")
	report("shared-secret-064", privateKeyHex: "0a0d622a47e48f6bc1038ace438c6f528aa00ad2bd1da5f13ee46bf5f633d71a", peerPublicKeyHex: "04249f4f517d7788d4622d762c4312101fba71fe2657f1f2c33c0613d2e21890b778f32da1b61ed9385cd52f5e7103f77b01b403fb6537a0c0e866195d0a448f34")
	report("shared-secret-065", privateKeyHex: "0a0d622a47e48f6bc1038ace438c6f528aa00ad2bd1da5f13ee46bf5f633d71a", peerPublicKeyHex: "04e9c0af0bf6dadd8fbe04c5cf3fce80683a9c267b0dc9850cbc1bcdfeb744c3516efe878223dd9dd924e83e3ab22c9bf304129ab59c1f3d30fb58b8770bf9d9bf")
	report("shared-secret-066", privateKeyHex: "0a0d622a47e48f6bc1038ace438c6f528aa00ad2bd1da5f13ee46bf5f633d71a", peerPublicKeyHex: "042b29f482bc43559f70447584919df1e03706ee63c987b6f7499ae29ab01ca828a4bd05d61f596e01623a8e2076c013889fbb0f9937e475a00ddd75e6a992e776")
	report("shared-secret-067", privateKeyHex: "0a0d622a47e48f6bc1038ace438c6f528aa00ad2bd1da5f13ee46bf5f633d71a", peerPublicKeyHex: "042766dc8e289fd27625d4765eb014aa54a44a7d5724050b9446e363071c260913aff9f1dc5e71b666501b24d70805cb8e1a01ee90ca84cb0c6f7ea453df85f520")
	report("shared-secret-068", privateKeyHex: "0a0d622a47e48f6bc1038ace438c6f528aa00ad2bd1da5f13ee46bf5f633d71a", peerPublicKeyHex: "04d33314f13a7fe8357bfd7a11799498e55bc782c6f434856d733b668e1d05448348dc5aae3f6431dd20c018f54b1d65fd1864ddfeea04ad7533971237c60ac0c7")
	report("shared-secret-069", privateKeyHex: "0a0d622a47e48f6bc1038ace438c6f528aa00ad2bd1da5f13ee46bf5f633d71a", peerPublicKeyHex: "043cbc1b31b43f17dc200dd70c2944c04c6cb1b082820c234a300b05b7763844c74fde0a4ef93887469793270eb2ff148287da9265b0334f9e2609aac16e8ad503")
	report("shared-secret-070", privateKeyHex: "0a0d622a47e48f6bc1038ace438c6f528aa00ad2bd1da5f13ee46bf5f633d71a", peerPublicKeyHex: "042830d96489ae24b79cad425056e82746f9e3f419ab9aa21ca1fbb11c7325e7d318abe66f575ee8a2f1c4a80e35260ae82ad7d6f661d15f06967930a585097ef7")
	report("shared-secret-071", privateKeyHex: "0a0d622a47e48f6bc1038ace438c6f528aa00ad2bd1da5f13ee46bf5f633d71a", peerPublicKeyHex: "04450b6b6e2097178e9d2850109518d28eb3b6ded2922a5452003bc2e4a4ec775c894e90f0df1b0e6cadb03b9de24f6a22d1bd0a4a58cd645c273cae1c619bfd61")
	report("shared-secret-072", privateKeyHex: "55d55f11bb8da1ea318bca7266f0376662441ea87270aa2077f1b770c4854a48", peerPublicKeyHex: "047fffffffffffffffffffffffeecf2230ffffffffffffffffffffffffffffffff00000001c7c30643abed0af0a49fe352cb483ff9b97dccdf427c658e8793240d")
	report("shared-secret-073", privateKeyHex: "55d55f11bb8da1ea318bca7266f0376662441ea87270aa2077f1b770c4854a48", peerPublicKeyHex: "04000000000000000000000000111124f4000000000000000000000000000000000000000d12d381b0760b1c50be8acf859385052c7f53cde67ce13759de3123a0")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
shared-secret-001 46fc62106420ff012e54a434fbdd2d25ccc5852060561e68040dd7778997bd7b
shared-secret-002 057d636096cb80b67a8c038c890e887d1adfa4195e9b3ce241c8a778c59cda67
shared-secret-003 2d457b78b4614132477618a5b077965ec90730a8c81a1c75d6d4ec68005d67ec
shared-secret-004 96441259534b80f6aee3d287a6bb17b5094dd4277d9e294f8fe73e48bf2a0024
shared-secret-005 19d44c8d63e8e8dd12c22a87b8cd4ece27acdde04dbf47f7f27537a6999a8e62
shared-secret-006 664e45d5bba4ac931cd65d52017e4be9b19a515f669bea4703542a2c525cd3d3
shared-secret-007 ca342daa50dc09d61be7c196c85e60a80c5cb04931746820be548cdde055679d
shared-secret-008 35aa9b52536a461bfde4e85fc756be928c7de97923f0416c7a3ac8f88b3d4489
shared-secret-009 605c16178a9bc875dcbff54d63fe00df699c03e8a888e9e94dfbab90b25f39b4
shared-secret-010 f96e40a1b72840854bb62bc13c40cc2795e373d4e715980b261476835a092e0b
shared-secret-011 8388fa79c4babdca02a8e8a34f9e43554976e420a4ad273c81b26e4228e9d3a3
shared-secret-012 72877cea33ccc4715038d4bcbdfe0e43f42a9e2c0c3b017fc2370f4b9acbda4a
shared-secret-013 e4e7408d85ff0e0e9c838003f28cdbd5247cdce31f32f62494b70e5f1bc36307
shared-secret-014 ed56bcf695b734142c24ecb1fc1bb64d08f175eb243a31f37b3d9bb4407f3b96
shared-secret-015 bc5c7055089fc9d6c89f83c1ea1ada879d9934b2ea28fcf4e4a7e984b28ad2cf
shared-secret-016 9a4e8e657f6b0e097f47954a63c75d74fcba71a30d83651e3e5a91aa7ccd8343
shared-secret-017 3ca1fc7ad858fb1a6aba232542f3e2a749ffc7203a2374a3f3d3267f1fc97b78
shared-secret-018 1aaabe7ee6e4a6fa732291202433a237df1b49bc53866bfbe00db96a0f58224f
shared-secret-019 430e6a4fba4449d700d2733e557f66a3bf3d50517c1271b1ddae1161b7ac798c
shared-secret-020 1ce9e6740529499f98d1f1d71329147a33df1d05e4765b539b11cf615d6974d3
shared-secret-021 4690e3743c07d643f1bc183636ab2a9cb936a60a802113c49bb1b3f2d0661660
shared-secret-022 30c2261bd0004e61feda2c16aa5e21ffa8d7e7f7dbf6ec379a43b48e4b36aeb0
shared-secret-023 2adae4a138a239dcd93c243a3803c3e4cf96e37fe14e6a9b717be9599959b11c
shared-secret-024 2e277ec30f5ea07d6ce513149b9479b96e07f4b6913b1b5c11305c1444a1bc0b
shared-secret-025 1e51373bd2c6044c129c436e742a55be2a668a85ae08441b6756445df5493857
shared-secret-026 53020d908b0219328b658b525f26780e3ae12bcd952bb25a93bc0895e1714285
shared-secret-027 0000000000000000000000000000000000000000000000000000000000000000
shared-secret-028 ffffffff00000001000000000000000000000000fffffffffffffffffffffffc
shared-secret-029 0000000000000000000000000000000000000000000000000000000000010000
shared-secret-030 0000000000000000000000000000000000000000000000000000000100000000
shared-secret-031 0000000000000000000000000000000000000000000000010000000000000000
shared-secret-032 0000000000000000000000000000000000000001000000000000000000000000
shared-secret-033 6522aed9ea48f2623b8eeae3e213b99da32e74c9421835804d374ce28fcca662
shared-secret-034 507442007322aa895340cba4abc2d730bfd0b16c2c79a46815f8780d2c55a2dd
shared-secret-035 085ec5a4af40176b63189069aeffcb229c96d3e046e0283ed2f9dac21b15ad3c
shared-secret-036 190c25f88ad9ae3a098e6cffe6fd0b1bea42114eb0cedd5868a45c5fe277dff3
shared-secret-037 5f177bfe19baaaee597e68b6a87a519e805e9d28a70cb72fd40f0fe5a754ba45
shared-secret-038 3476a8fa9c7448124aa945a2e1103eb91f95c7b4d91aaa1c35b8965191fc0fe0
shared-secret-039 5555555555555555555555555555555555555555555555555555555555555554
shared-secret-040 aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa9
shared-secret-041 3333333333333333333333333333333333333333333333333333333333333333
shared-secret-042 cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc
shared-secret-043 0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f
shared-secret-044 f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0
shared-secret-045 00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00f9
shared-secret-046 ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00feff
shared-secret-047 7fff0001fffc0007fff0001fffc0007fff0001fffc0007fff0001fffc0007fff
shared-secret-048 8000fffe0003fff8000fffe0003fff8000fffe0003fff8000fffe0003fff7ffe
shared-secret-049 0000ffff0000ffff0000ffff0000ffff0000ffff0000ffff0000ffff0000fffe
shared-secret-050 ffff0000ffff0000ffff0000ffff0000ffff0000ffff0000ffff0000fffefffd
shared-secret-051 8000003ffffff0000007fffffe000000ffffffc000001ffffff8000003fffffc
shared-secret-052 7fffffe000000ffffffc000001ffffff8000003ffffff0000007fffffdfffffe
shared-secret-053 000003ffffff0000003ffffff0000003ffffff0000003ffffff0000003ffffff
shared-secret-054 fffffc000000ffffffc000000ffffffc000000ffffffc000000ffffffbffffff
shared-secret-055 ffff00000003fffffff00000003fffffff00000003fffffff00000003fffffff
shared-secret-056 0000fffffffc0000000fffffffc0000000fffffffc0000000fffffffbfffffff
shared-secret-057 ff00000001fffffffc00000007fffffff00000001fffffffc00000007fffffff
shared-secret-058 00fffffffe00000003fffffff80000000fffffffe00000003fffffff80000000
shared-secret-059 00000000ffffffff00000000ffffffff00000000ffffffff00000000fffffffc
shared-secret-060 ffffffff00000000ffffffff00000000ffffffff00000000ffffffff00000000
shared-secret-061 ffffffff00000000000000ffffffffffffff00000000000000ffffffffffffff
shared-secret-062 00000000ffffffffffffff00000000000000ffffffffffffff00000000000000
shared-secret-063 0000000000000000ffffffffffffffff0000000000000000fffffffffffffffd
shared-secret-064 7fffffffffffffff0000000000000000ffffffffffffffff0000000000000000
shared-secret-065 5555555500000000555555555555555555555555aaaaaaaaaaaaaaaaaaaaaaab
shared-secret-066 3333333300000000333333333333333333333333666666666666666666666666
shared-secret-067 249249246db6db6ddb6db6db6db6db6db6db6db7000000000000000000000000
shared-secret-068 1c71c71c5555555571c71c71c71c71c71c71c71c8e38e38e38e38e38e38e38e4
shared-secret-069 7fffffffffffffffffffffffeecf2230ffffffffffffffffffffffffffffffff
shared-secret-070 000000000000000000000000111124f400000000000000000000000000000000
shared-secret-071 000000000000000000000001ea77d449ffffffffffffffffffffffffffffffff
shared-secret-072 6fd26661851a8de3c6d06f834ef3acb8f2a5f9c136a985ffe10d5eeb51edcfa3
shared-secret-073 f1f0e43b374feb7e7f96d4ffe7519fa8bb6c3cfd25f6f87dab2623d2a2d33851
```

<!-- test: ecdh.shared-secret-2 -->
`EcdhP256.sharedSecret` against the canra ecdh-p256 vectors: NIST CAVS, Wycheproof and constructed refusals. Vectors shared-secret-074 to shared-secret-146 (73).
```maxon
function digit(symbol Byte) returns Byte
	if symbol >= 0x61 'letter'
		return symbol - 0x57
	end 'letter'

	return symbol - 0x30
end 'digit'

function fromHex(hex String) returns ByteArray
	var bytes = ByteArray.create()
	var high = 0 as Byte
	var isHigh = true

	for symbol in hex.bytes() 'eachSymbol'
		if isHigh 'first'
			high = digit(symbol)
		end 'first' else 'second'
			bytes.push(high * 16 + digit(symbol))
		end 'second'

		isHigh = not isHigh
	end 'eachSymbol'

	return bytes
end 'fromHex'

function toHex(bytes ByteArray) returns String
	var text = ""

	for byte in bytes 'eachByte'
		text.append("{byte:02x}")
	end 'eachByte'

	return text
end 'toHex'

function errorName(failure EcdhError) returns String
	return match failure 'name'
		invalidPrivateKey gives "invalidPrivateKey"
		invalidPublicKey gives "invalidPublicKey"
	end 'name'
end 'errorName'

function report(id String, privateKeyHex String, peerPublicKeyHex String)
	if let secret = try EcdhP256.sharedSecret(fromHex(privateKeyHex), peerPublicKey: fromHex(peerPublicKeyHex)) 'agreed'
		print("{id} {toHex(secret)}\n")
	end 'agreed' else (failure) 'refused'
		print("{id} {errorName(failure)}\n")
	end 'refused'
end 'report'

function main() returns ExitCode
	report("shared-secret-074", privateKeyHex: "55d55f11bb8da1ea318bca7266f0376662441ea87270aa2077f1b770c4854a48", peerPublicKeyHex: "04000000000000000000000001ea77d449ffffffffffffffffffffffffffffffff000000007afbc0b325e820646dec622fb558a51c342aa257f4b6a8ec5ddf144f")
	report("shared-secret-075", privateKeyHex: "0a0d622a47e48f6bc1038ace438c6f528aa00ad2bd1da5f13ee46bf5f633d71a", peerPublicKeyHex: "043cbc1b31b43f17dc200dd70c2944c04c6cb1b082820c234a300b05b7763844c7b021f5b006c778ba686cd8f14d00eb7d78256d9b4fccb061d9f6553e91752afc")
	report("shared-secret-076", privateKeyHex: "0a0d622a47e48f6bc1038ace438c6f528aa00ad2bd1da5f13ee46bf5f633d71a", peerPublicKeyHex: "042830d96489ae24b79cad425056e82746f9e3f419ab9aa21ca1fbb11c7325e7d3e754198fa8a1175e0e3b57f1cad9f517d528290a9e2ea0f96986cf5a7af68108")
	report("shared-secret-077", privateKeyHex: "0a0d622a47e48f6bc1038ace438c6f528aa00ad2bd1da5f13ee46bf5f633d71a", peerPublicKeyHex: "04450b6b6e2097178e9d2850109518d28eb3b6ded2922a5452003bc2e4a4ec775c76b16f0e20e4f194524fc4621db095dd2e42f5b6a7329ba3d8c351e39e64029e")
	report("shared-secret-078", privateKeyHex: "55d55f11bb8da1ea318bca7266f0376662441ea87270aa2077f1b770c4854a48", peerPublicKeyHex: "047fffffffffffffffffffffffeecf2230fffffffffffffffffffffffffffffffffffffffd383cf9bd5412f50f5b601cad34b7c00746823320bd839a71786cdbf2")
	report("shared-secret-079", privateKeyHex: "55d55f11bb8da1ea318bca7266f0376662441ea87270aa2077f1b770c4854a48", peerPublicKeyHex: "04000000000000000000000000111124f400000000000000000000000000000000fffffff1ed2c7e5089f4e3af4175307a6c7afad480ac3219831ec8a621cedc5f")
	report("shared-secret-080", privateKeyHex: "55d55f11bb8da1ea318bca7266f0376662441ea87270aa2077f1b770c4854a48", peerPublicKeyHex: "04000000000000000000000001ea77d449fffffffffffffffffffffffffffffffffffffffe85043f4dda17df9b92139dd04aa75ae4cbd55da80b495713a220ebb0")
	report("shared-secret-081", privateKeyHex: "0a0d622a47e48f6bc1038ace438c6f528aa00ad2bd1da5f13ee46bf5f633d71a", peerPublicKeyHex: "049a0f0e3dd31417bbd9e298bc068ab6d5c36733af26ed67676f410c804b8b2ca1b02c82f3a61a376db795626e9400557112273a36cddb08caaa43953965454730")
	report("shared-secret-082", privateKeyHex: "0a0d622a47e48f6bc1038ace438c6f528aa00ad2bd1da5f13ee46bf5f633d71a", peerPublicKeyHex: "048e5d22d5e53ec797c55ecd68a08a7c3361cd99ca7fad1a68ea802a6a4cb58a918ea7a07023ef67677024bd3841e187c64b30a30a3750eb2ee873fbe58fa1357b")
	report("shared-secret-083", privateKeyHex: "0a0d622a47e48f6bc1038ace438c6f528aa00ad2bd1da5f13ee46bf5f633d71a", peerPublicKeyHex: "04293aa349b934ab2c839cf54b8a737df2304ef9b20fa494e31ad62b315dd6a53c118182b85ef466eb9a8e87f9661f7d017984c15ea82043f536d1ee6a6d95b509")
	report("shared-secret-084", privateKeyHex: "55d55f11bb8da1ea318bca7266f0376662441ea87270aa2077f1b770c4854a48", peerPublicKeyHex: "047fffffffffffffffffffffffca089011ffffffffffffffffffffffffffffffff267bfdf8a61148decd80283732dd4c1095e4bb40b9658408208dc1147fffffff")
	report("shared-secret-085", privateKeyHex: "55d55f11bb8da1ea318bca7266f0376662441ea87270aa2077f1b770c4854a48", peerPublicKeyHex: "040000000000000000000000001f6bd1e5000000000000000000000000000000004096edd6871c320cb8a9f4531751105c97b4c257811bbc32963eaf39ffffffff")
	report("shared-secret-086", privateKeyHex: "55d55f11bb8da1ea318bca7266f0376662441ea87270aa2077f1b770c4854a48", peerPublicKeyHex: "04000000000000000000000002099f55d5ffffffffffffffffffffffffffffffff152c1a22d823a27855ed03f8e2ab5038bb1df4d87e43865f2daf6948ffffffff")
	report("shared-secret-087", privateKeyHex: "0a0d622a47e48f6bc1038ace438c6f528aa00ad2bd1da5f13ee46bf5f633d71a", peerPublicKeyHex: "049a0f0e3dd31417bbd9e298bc068ab6d5c36733af26ed67676f410c804b8b2ca14fd37d0b59e5c893486a9d916bffaa8eedd8c5ca3224f73555bc6ac69abab8cf")
	report("shared-secret-088", privateKeyHex: "0a0d622a47e48f6bc1038ace438c6f528aa00ad2bd1da5f13ee46bf5f633d71a", peerPublicKeyHex: "048e5d22d5e53ec797c55ecd68a08a7c3361cd99ca7fad1a68ea802a6a4cb58a9171585f8edc1098998fdb42c7be1e7839b4cf5cf6c8af14d1178c041a705eca84")
	report("shared-secret-089", privateKeyHex: "0a0d622a47e48f6bc1038ace438c6f528aa00ad2bd1da5f13ee46bf5f633d71a", peerPublicKeyHex: "04293aa349b934ab2c839cf54b8a737df2304ef9b20fa494e31ad62b315dd6a53cee7e7d46a10b99156571780699e082fe867b3ea257dfbc0ac92e1195926a4af6")
	report("shared-secret-090", privateKeyHex: "55d55f11bb8da1ea318bca7266f0376662441ea87270aa2077f1b770c4854a48", peerPublicKeyHex: "047fffffffffffffffffffffffca089011ffffffffffffffffffffffffffffffffd984020659eeb722327fd7c8cd22b3ef6a1b44c0469a7bf7df723eeb80000000")
	report("shared-secret-091", privateKeyHex: "55d55f11bb8da1ea318bca7266f0376662441ea87270aa2077f1b770c4854a48", peerPublicKeyHex: "040000000000000000000000001f6bd1e500000000000000000000000000000000bf69122878e3cdf447560bace8aeefa3684b3da97ee443cd69c150c600000000")
	report("shared-secret-092", privateKeyHex: "55d55f11bb8da1ea318bca7266f0376662441ea87270aa2077f1b770c4854a48", peerPublicKeyHex: "04000000000000000000000002099f55d5ffffffffffffffffffffffffffffffffead3e5dc27dc5d88aa12fc071d54afc744e20b2881bc79a0d25096b700000000")
	report("shared-secret-093", privateKeyHex: "55d55f11bb8da1ea318bca7266f0376662441ea87270aa2077f1b770c4854a48", peerPublicKeyHex: "04000000000000000000000000000000000000000000000000000000000000000066485c780e2f83d72433bd5d84a06bb6541c2af31dae871728bf856a174f93f4")
	report("shared-secret-094", privateKeyHex: "55d55f11bb8da1ea318bca7266f0376662441ea87270aa2077f1b770c4854a48", peerPublicKeyHex: "04ffffffff00000001000000000000000000000000fffffffffffffffffffffffc19719bebf6aea13f25c96dfd7c71f5225d4c8fc09eb5a0ab9f39e9178e55c121")
	report("shared-secret-095", privateKeyHex: "55d55f11bb8da1ea318bca7266f0376662441ea87270aa2077f1b770c4854a48", peerPublicKeyHex: "04000000000000000000000000000000000000000000000000000000000001000073ca30641acabe581170b4d5253f7a342b039a87ab2447badad08ece2cf19f52")
	report("shared-secret-096", privateKeyHex: "55d55f11bb8da1ea318bca7266f0376662441ea87270aa2077f1b770c4854a48", peerPublicKeyHex: "040000000000000000000000000000000000000000000000000000000100000000328bbc85099acc7685cf4bb5e18d72c57271615ccd46377bfacb0e8501aa76fb")
	report("shared-secret-097", privateKeyHex: "55d55f11bb8da1ea318bca7266f0376662441ea87270aa2077f1b770c4854a48", peerPublicKeyHex: "04000000000000000000000000000000000000000000000001000000000000000010048b687675af9815297d73a7f25b1a3c42d33c64b466991a7573b4954aa0b0")
	report("shared-secret-098", privateKeyHex: "55d55f11bb8da1ea318bca7266f0376662441ea87270aa2077f1b770c4854a48", peerPublicKeyHex: "0400000000000000000000000000000000000000010000000000000000000000007d12de58d54423eb85ae8d157ae416fb004a7eb522ac1b67047ef3cdf9acdc3f")
	report("shared-secret-099", privateKeyHex: "55d55f11bb8da1ea318bca7266f0376662441ea87270aa2077f1b770c4854a48", peerPublicKeyHex: "046522aed9ea48f2623b8eeae3e213b99da32e74c9421835804d374ce28fcca6622c7fda3c84b704f27ffc0b2ab350d8cbc33a34df0c811155ed4389d2710e3fa9")
	report("shared-secret-100", privateKeyHex: "55d55f11bb8da1ea318bca7266f0376662441ea87270aa2077f1b770c4854a48", peerPublicKeyHex: "04507442007322aa895340cba4abc2d730bfd0b16c2c79a46815f8780d2c55a2dd4619d69f9940f51663aa12381bc7cf678bd1a72a49fbc11b0b69cb22d1af9f2d")
	report("shared-secret-101", privateKeyHex: "55d55f11bb8da1ea318bca7266f0376662441ea87270aa2077f1b770c4854a48", peerPublicKeyHex: "04085ec5a4af40176b63189069aeffcb229c96d3e046e0283ed2f9dac21b15ad3c7859f97cb6e203f46bf3438f61282325e94e681b60b5669788aeb0655bf19d38")
	report("shared-secret-102", privateKeyHex: "55d55f11bb8da1ea318bca7266f0376662441ea87270aa2077f1b770c4854a48", peerPublicKeyHex: "04190c25f88ad9ae3a098e6cffe6fd0b1bea42114eb0cedd5868a45c5fe277dff321b8342ef077bc6724112403eaee5a15b4c31a71589f02ded09cd99cc5db9c83")
	report("shared-secret-103", privateKeyHex: "55d55f11bb8da1ea318bca7266f0376662441ea87270aa2077f1b770c4854a48", peerPublicKeyHex: "045f177bfe19baaaee597e68b6a87a519e805e9d28a70cb72fd40f0fe5a754ba4562ca1103f70a2006cd1f67f5f6a3580b29dc446abc90e0e910c1e05a9aa788cd")
	report("shared-secret-104", privateKeyHex: "55d55f11bb8da1ea318bca7266f0376662441ea87270aa2077f1b770c4854a48", peerPublicKeyHex: "043476a8fa9c7448124aa945a2e1103eb91f95c7b4d91aaa1c35b8965191fc0fe04ddac138688df5123fa5760d670247893f9ad5cad5e88895a0ae7fdf97b8f412")
	report("shared-secret-105", privateKeyHex: "55d55f11bb8da1ea318bca7266f0376662441ea87270aa2077f1b770c4854a48", peerPublicKeyHex: "0455555555555555555555555555555555555555555555555555555555555555540a46712790e63f981f0103b2a609fa691c5291a17974eb0c3b518faac21535d6")
	report("shared-secret-106", privateKeyHex: "55d55f11bb8da1ea318bca7266f0376662441ea87270aa2077f1b770c4854a48", peerPublicKeyHex: "04aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa9389142d4676cee2332eb8909fa13f95f20cdb3acc831a231802a4858a973ca63")
	report("shared-secret-107", privateKeyHex: "55d55f11bb8da1ea318bca7266f0376662441ea87270aa2077f1b770c4854a48", peerPublicKeyHex: "04333333333333333333333333333333333333333333333333333333333333333311489212aff79cbed5b6440161d57c98a34e6b80ec3854c3b2855af35f0d17e1")
	report("shared-secret-108", privateKeyHex: "55d55f11bb8da1ea318bca7266f0376662441ea87270aa2077f1b770c4854a48", peerPublicKeyHex: "04cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc75552486fd8e6bc424492204207fece59d93fb124390497f841d9c808c8cc3ec")
	report("shared-secret-109", privateKeyHex: "55d55f11bb8da1ea318bca7266f0376662441ea87270aa2077f1b770c4854a48", peerPublicKeyHex: "040f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f03425ba0d8a018b42861e816c79a17d3b47df8f5f7107c181168e9edc62cb3cf")
	report("shared-secret-110", privateKeyHex: "55d55f11bb8da1ea318bca7266f0376662441ea87270aa2077f1b770c4854a48", peerPublicKeyHex: "04f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f017b41bf72c9f5ec204dba0deb5dfaa43b793a83b243060d0375b969e0416bd56")
	report("shared-secret-111", privateKeyHex: "55d55f11bb8da1ea318bca7266f0376662441ea87270aa2077f1b770c4854a48", peerPublicKeyHex: "0400ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00f918461138bca461c00fc474da0e4e35309be520e2e109e17a1cecb892b96598f6")
	report("shared-secret-112", privateKeyHex: "55d55f11bb8da1ea318bca7266f0376662441ea87270aa2077f1b770c4854a48", peerPublicKeyHex: "04ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00feff2d6c62a47883c5899cac06b1367c21948689d522674a60f43f4b808adf618b0b")
	report("shared-secret-113", privateKeyHex: "55d55f11bb8da1ea318bca7266f0376662441ea87270aa2077f1b770c4854a48", peerPublicKeyHex: "047fff0001fffc0007fff0001fffc0007fff0001fffc0007fff0001fffc0007fff2e2213caf03033e0fd0f7951154f6e6c3a9244a72faca65e9ce9eeb5c8e1cea9")
	report("shared-secret-114", privateKeyHex: "55d55f11bb8da1ea318bca7266f0376662441ea87270aa2077f1b770c4854a48", peerPublicKeyHex: "048000fffe0003fff8000fffe0003fff8000fffe0003fff8000fffe0003fff7ffe1d3b684a87958143f250bf5a2f7859fd036620b4dc18dacd1d0c8c42eeb2e54a")
	report("shared-secret-115", privateKeyHex: "55d55f11bb8da1ea318bca7266f0376662441ea87270aa2077f1b770c4854a48", peerPublicKeyHex: "040000ffff0000ffff0000ffff0000ffff0000ffff0000ffff0000ffff0000fffe7ccb5c10c028f331230dd0a005e9faf37864c198c5bf7d91bdd71624520d4b04")
	report("shared-secret-116", privateKeyHex: "55d55f11bb8da1ea318bca7266f0376662441ea87270aa2077f1b770c4854a48", peerPublicKeyHex: "04ffff0000ffff0000ffff0000ffff0000ffff0000ffff0000ffff0000fffefffd620bbe0a621748ea2b0e0dfa463169e7c805412f4374dc9755c4db93989a7529")
	report("shared-secret-117", privateKeyHex: "55d55f11bb8da1ea318bca7266f0376662441ea87270aa2077f1b770c4854a48", peerPublicKeyHex: "048000003ffffff0000007fffffe000000ffffffc000001ffffff8000003fffffc0c3527bd081c1c07b313bc1a0c3f845fb2fe22557699ccc8f1354e61a27b7f88")
	report("shared-secret-118", privateKeyHex: "55d55f11bb8da1ea318bca7266f0376662441ea87270aa2077f1b770c4854a48", peerPublicKeyHex: "047fffffe000000ffffffc000001ffffff8000003ffffff0000007fffffdfffffe232aa1c67f77cfb00e73898cf431831073c6b2a4bfa1135881a0595720759ece")
	report("shared-secret-119", privateKeyHex: "55d55f11bb8da1ea318bca7266f0376662441ea87270aa2077f1b770c4854a48", peerPublicKeyHex: "04000003ffffff0000003ffffff0000003ffffff0000003ffffff0000003ffffff1582fa32e2d4a89dfcfb3d0b149f667dba3329490f4d64ee2ad586c0c9e8c508")
	report("shared-secret-120", privateKeyHex: "55d55f11bb8da1ea318bca7266f0376662441ea87270aa2077f1b770c4854a48", peerPublicKeyHex: "04fffffc000000ffffffc000000ffffffc000000ffffffc000000ffffffbffffff73f471909cfde3db2c13523bcf372877c28d23ed1ff6a3326df6bbc4b756723e")
	report("shared-secret-121", privateKeyHex: "55d55f11bb8da1ea318bca7266f0376662441ea87270aa2077f1b770c4854a48", peerPublicKeyHex: "04ffff00000003fffffff00000003fffffff00000003fffffff00000003fffffff2c63650e6a5d332e2987dd09a79008e8faabbd37e49cb016bfb92c8cd0f5da77")
	report("shared-secret-122", privateKeyHex: "55d55f11bb8da1ea318bca7266f0376662441ea87270aa2077f1b770c4854a48", peerPublicKeyHex: "040000fffffffc0000000fffffffc0000000fffffffc0000000fffffffbfffffff71664ef90d87525ddf7815e676ae3b96c25ed9e7bdaa05da96227a1fe38c3d11")
	report("shared-secret-123", privateKeyHex: "55d55f11bb8da1ea318bca7266f0376662441ea87270aa2077f1b770c4854a48", peerPublicKeyHex: "04ff00000001fffffffc00000007fffffff00000001fffffffc00000007fffffff5df80fc6cae26b6c1952fbd00ed174ee1209d069335f5b48588e29e80b9191ad")
	report("shared-secret-124", privateKeyHex: "55d55f11bb8da1ea318bca7266f0376662441ea87270aa2077f1b770c4854a48", peerPublicKeyHex: "0400fffffffe00000003fffffff80000000fffffffe00000003fffffff8000000007bc25fb4d2b4cd12a16515725d64cdbd5bc86b7f84ef005c9e51d0700a3a4b8")
	report("shared-secret-125", privateKeyHex: "55d55f11bb8da1ea318bca7266f0376662441ea87270aa2077f1b770c4854a48", peerPublicKeyHex: "0400000000ffffffff00000000ffffffff00000000ffffffff00000000fffffffc4de9de6942e770033f5f1defbb36249e6bdf45a33e2ef4ec75a7ad880b5432c8")
	report("shared-secret-126", privateKeyHex: "55d55f11bb8da1ea318bca7266f0376662441ea87270aa2077f1b770c4854a48", peerPublicKeyHex: "04ffffffff00000000ffffffff00000000ffffffff00000000ffffffff00000000462e0f7377a81a3a47a28e62dd8105687e457496c61fdce49ba1aaeefcf10b44")
	report("shared-secret-127", privateKeyHex: "55d55f11bb8da1ea318bca7266f0376662441ea87270aa2077f1b770c4854a48", peerPublicKeyHex: "04ffffffff00000000000000ffffffffffffff00000000000000ffffffffffffff7a116c964a4cd60668bf89cffe157714a3ce21b93b3ca607c8a5b93ac54ffc0a")
	report("shared-secret-128", privateKeyHex: "55d55f11bb8da1ea318bca7266f0376662441ea87270aa2077f1b770c4854a48", peerPublicKeyHex: "0400000000ffffffffffffff00000000000000ffffffffffffff0000000000000012a69aaa5d3ac4cee07592ea0989dd9f426a0e4d16807b6c6d04baa628a659c0")
	report("shared-secret-129", privateKeyHex: "55d55f11bb8da1ea318bca7266f0376662441ea87270aa2077f1b770c4854a48", peerPublicKeyHex: "040000000000000000ffffffffffffffff0000000000000000fffffffffffffffd137bd006820a8be23475ea25d8fa05c8ed47fca1236cd5d1f07667db6fcd2979")
	report("shared-secret-130", privateKeyHex: "55d55f11bb8da1ea318bca7266f0376662441ea87270aa2077f1b770c4854a48", peerPublicKeyHex: "047fffffffffffffff0000000000000000ffffffffffffffff0000000000000000322a5313a994312a3f047d5a9c6cdeda173f1d3b35143b3b1a594f7dbe544d0e")
	report("shared-secret-131", privateKeyHex: "55d55f11bb8da1ea318bca7266f0376662441ea87270aa2077f1b770c4854a48", peerPublicKeyHex: "045555555500000000555555555555555555555555aaaaaaaaaaaaaaaaaaaaaaab79dfbfa5e66fbea30e75ecc703e65db43e027416c01f6e20fe129be0e43457d8")
	report("shared-secret-132", privateKeyHex: "55d55f11bb8da1ea318bca7266f0376662441ea87270aa2077f1b770c4854a48", peerPublicKeyHex: "04333333330000000033333333333333333333333366666666666666666666666610a4ea9bc365e1b224b173e9fd688a5234932ec90a5cb9ecc0a22a109927e73b")
	report("shared-secret-133", privateKeyHex: "55d55f11bb8da1ea318bca7266f0376662441ea87270aa2077f1b770c4854a48", peerPublicKeyHex: "04249249246db6db6ddb6db6db6db6db6db6db6db70000000000000000000000005ebbe906d14cfa3e7e90d397973f093a6c678a23bcb2896189ffece5a1ca398e")
	report("shared-secret-134", privateKeyHex: "55d55f11bb8da1ea318bca7266f0376662441ea87270aa2077f1b770c4854a48", peerPublicKeyHex: "041c71c71c5555555571c71c71c71c71c71c71c71c8e38e38e38e38e38e38e38e46ef488581fbbd6b4ef78d03cdd161272243fdcc10979fd8e8fd199b98a63d378")
	report("shared-secret-135", privateKeyHex: "e461c5b5e63d75b4c8c123bf8b9cd45e712af08f7e2e494a8f255ac9d80e058b", peerPublicKeyHex: "04851eb1b8699d1cc01c6b6048139797dab3981c924f9ef81bd1f464c3182c870901285b2ae7cbcabfd5bccb9441eb3376b491595f60a1ec21dbe9f972bc32abbe")
	report("shared-secret-136", privateKeyHex: "e461c5b5e63d75b4c8c123bf8b9cd45e712af08f7e2e494a8f255ac9d80e058b", peerPublicKeyHex: "04f576e22747f6c77a8f685a7a7b527363e0fe7d351a9e3f5923fff2bf97e91af41e575d400331cfc9f97180d8a48c418c5a982609fab88937d2590bfdaf37d6a4")
	report("shared-secret-137", privateKeyHex: "e461c5b5e63d75b4c8c123bf8b9cd45e712af08f7e2e494a8f255ac9d80e058b", peerPublicKeyHex: "04310785fa42c37bd200781d70e3859b39b2fa1d286a3b6c4f605a41e045634b81b8d32564a7a232c5d47407a7795c01eda819f23a5a190149eb8c9abf94ed7c49")
	report("shared-secret-138", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "0481bfb55b010b1bdf08b8d9d8590087aa278e28febff3b05632eeff09011c5579732d0e65267ea28b7af8cfcb148936c2af8664cbb4f04e188148a1457400c2a7")
	report("shared-secret-139", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "0402495ac9f43e45aae30d3366e351cc08828cf3e11cc3b7209fbd1730c4a14f4e4aaa98dbe0d94caa44538fcf7949eb44debb7bcd228a54d9cd9b3c2eddf94611")
	report("shared-secret-140", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "04efe7754ed4c0b3c1dd301bc1ed69800aa2ff5d51fb85937715e60d2e7bcada8e4ea7e547a04c3869106b56245c27da9737b9e8160c05fb0d86040276708fb9fb")
	report("shared-secret-141", privateKeyHex: "e461c5b5e63d75b4c8c123bf8b9cd45e712af08f7e2e494a8f255ac9d80e058b", peerPublicKeyHex: "0466aeda76d73983caf2de921af389268ceffd278fb9eccf928702b1f4271b43f1d646d6cf8bc11a0ef97cd64c08a11f72f94af4e6b3c9870b8eb3a5b036c04fb8")
	report("shared-secret-142", privateKeyHex: "e461c5b5e63d75b4c8c123bf8b9cd45e712af08f7e2e494a8f255ac9d80e058b", peerPublicKeyHex: "04f483602787972cf7b7582d8e2f2c7998e537ca57eba69718b68d96240f035b1b55dcb4f7be7b79c469ba8a0b104900d7053ffcac04f394be925fe0cff2e969c3")
	report("shared-secret-143", privateKeyHex: "e461c5b5e63d75b4c8c123bf8b9cd45e712af08f7e2e494a8f255ac9d80e058b", peerPublicKeyHex: "04579be77fa6698c4baf0132d2f7993ed76dfd36d78b584605e1be024595e3ad51c069473e1b25fd94a42b68ccb3004a851593a3af78f922d90920d8a877c365f3")
	report("shared-secret-144", privateKeyHex: "e461c5b5e63d75b4c8c123bf8b9cd45e712af08f7e2e494a8f255ac9d80e058b", peerPublicKeyHex: "047f8128d883be31873efedab699206f889b776be270e163df43035409fa5215f1f36c3581f8f8d569a4323f8700ca09792150421521e2ef0a96ae9a4caed0e02c")
	report("shared-secret-145", privateKeyHex: "e461c5b5e63d75b4c8c123bf8b9cd45e712af08f7e2e494a8f255ac9d80e058b", peerPublicKeyHex: "04ee5d94bfb5db9cfe511a3aa70bafc93db93107383c12f5bebffd90c621a2051db4fce13261d54ab29c3c699b77358edb54c71801f3fe2a0d5cdf2ee3f184b4d6")
	report("shared-secret-146", privateKeyHex: "e461c5b5e63d75b4c8c123bf8b9cd45e712af08f7e2e494a8f255ac9d80e058b", peerPublicKeyHex: "040ec4f8845226e864c088505d721a2155190d4caa9a1f5a3f0afdf2ff49cf6d8bad01fd3808aa03442cf5396e10e13efd24cbd5a52862822eb62dfd27b5040600")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
shared-secret-074 1b085213a9c89d353e1111af078c38c502b7b4771efba51f589b5be243417bdc
shared-secret-075 7fffffffffffffffffffffffeecf2230ffffffffffffffffffffffffffffffff
shared-secret-076 000000000000000000000000111124f400000000000000000000000000000000
shared-secret-077 000000000000000000000001ea77d449ffffffffffffffffffffffffffffffff
shared-secret-078 6fd26661851a8de3c6d06f834ef3acb8f2a5f9c136a985ffe10d5eeb51edcfa3
shared-secret-079 f1f0e43b374feb7e7f96d4ffe7519fa8bb6c3cfd25f6f87dab2623d2a2d33851
shared-secret-080 1b085213a9c89d353e1111af078c38c502b7b4771efba51f589b5be243417bdc
shared-secret-081 7fffffffffffffffffffffffca089011ffffffffffffffffffffffffffffffff
shared-secret-082 0000000000000000000000001f6bd1e500000000000000000000000000000000
shared-secret-083 000000000000000000000002099f55d5ffffffffffffffffffffffffffffffff
shared-secret-084 44236c8b9505a19d48774a3903c0292759b0f826e6ac092ff898d87e53d353fc
shared-secret-085 3ebbace1098a81949d5605dd94a7aa88dc396c2c23e01a9c8cca5bb07bfbb6a1
shared-secret-086 67cb63566c7ceb12fdd85ce9d2f77c359242bbaa0ea1bf3cf510a4a26591d1f1
shared-secret-087 7fffffffffffffffffffffffca089011ffffffffffffffffffffffffffffffff
shared-secret-088 0000000000000000000000001f6bd1e500000000000000000000000000000000
shared-secret-089 000000000000000000000002099f55d5ffffffffffffffffffffffffffffffff
shared-secret-090 44236c8b9505a19d48774a3903c0292759b0f826e6ac092ff898d87e53d353fc
shared-secret-091 3ebbace1098a81949d5605dd94a7aa88dc396c2c23e01a9c8cca5bb07bfbb6a1
shared-secret-092 67cb63566c7ceb12fdd85ce9d2f77c359242bbaa0ea1bf3cf510a4a26591d1f1
shared-secret-093 cfe4077c8730b1c9384581d36bff5542bc417c9eff5c2afcb98cc8829b2ce848
shared-secret-094 4c25702d10cd12ec7e77c4f1979237976d6aa809c645d75a59d399a52377eb7b
shared-secret-095 4cfd639fd80c84b935c33cfaacceeb256b2f7e273c3ae5f0ff92b74957376f22
shared-secret-096 581bb2ea633af1e59b15c1342d5e14adb58f613a231149f7df7a69c0eca95b2c
shared-secret-097 9920473de58a319ebd87d57f4f6c09093c5e0072c01bc8df0dc60b8a7a684c6f
shared-secret-098 d9cd0d10ed4d3dc01f4216d229e21c6a447227a5bc2592c359b2d689a2dee3a8
shared-secret-099 af8bc612853197a6ee8b4af7300c0bc623f0cf4e4b301f47d66c27e8ecce9268
shared-secret-100 4e173a80907f361fe5a5d335ba7685d5eba93e9dfc8d8fcdb1dcd2d2bde27507
shared-secret-101 d874b55678d0a04d216c31b02f3ad1f30c92caaf168f34e3a743356d9276e993
shared-secret-102 11a8582057463fc76fda3ab8087eb0a420b0d601bb3134165a369646931e52a6
shared-secret-103 73220471ec8bad99a297db488a34a259f9bc891ffaf09922e6b5001f5df67018
shared-secret-104 f4e5f1757c8a03a3aec57b598b66a93bb356a6917e26b60489c0d06236ac6a37
shared-secret-105 be033e3db6216d3b3f9d66c96ca3b24475813ba8eb7820d7d6ff29c72fd39c64
shared-secret-106 a0da0b184f59ab0fdc0a6b03157c1b75269b284608b74a6e826f77104c42a9bd
shared-secret-107 ac03527f79d801081da4c0adcbba92e983ed46254354fd98e23ab659ee02a6cd
shared-secret-108 0d2759dae57c906a8cca8541a56f429a6ffedf028212f7f0ad06b696ee655caf
shared-secret-109 ff6e930d08290769ef982bc568b0b45f88b7d5219b00667820793b3e824c1615
shared-secret-110 bd106c43e45272091553f476e015fbb0982764c018d5f0666834ae0dc5dca131
shared-secret-111 b661d2bf35efdd264f32a3bb254cd6443e8115a297aba1d31610495d7c560dd5
shared-secret-112 e818ea3c65c24330b7b8894680fe349b17f450e9d878292f1390ead78085ed1f
shared-secret-113 55d0a203e22ffb523c8d2705060cee9d28308b51f184beefc518cff690bad346
shared-secret-114 50af11f10d130df5f9adcdd0103ff3d42808582487330932c2546022bf7c6a18
shared-secret-115 ec90224ee61b2ed7cb6b9716e248aa099dbf1eae41d1ca477e8e5ba4a512913e
shared-secret-116 cf1acfa0595265623fbfee5d8498e609b6590760d1b04f31f3470cd1af3ba9dd
shared-secret-117 967e55f853dad60f678bd35a46bd3c7dc6cf7d18526ac0890af80ade84461005
shared-secret-118 f21a20024d8315ae877f90af5b0c68b6a50214fea797942870c35f4ca127e591
shared-secret-119 06fa1059935e47a9fd667e13f469614eb257cc9a7e3fc599bfb92780d59b146d
shared-secret-120 2e83d496538e7bd16b530d93b960317394b5f8b9da5128a03d08b3c8fbb46561
shared-secret-121 e3c18e7d7377dc540bc45c08d389bdbe255fa80ca8faf1ef6b94d52049987d21
shared-secret-122 bc0a152832a8972bbeceb522e661e5249cc7455bf93cba324e810ab62a2e58c1
shared-secret-123 f503ac65637e0f17cb4408961cb882c875e4c6ef7a548d2d52d8c2f681838c55
shared-secret-124 68e8765c71a63f3458e8edb2e52f499a1de13ae1ff1c95f18c4adad9d898667a
shared-secret-125 4060db7a9bf8ca17a5ffa614a85f9a25ed6f1854dc0f2396b9e8439ea8807a55
shared-secret-126 9e6b02ef374c5b7c8d1021d1e0520ef5f6e3cb7e454d6630bd270decc0343f1e
shared-secret-127 516d6d329b095a7c7e93b4023d4d05020c1445ef1ddcb3347b3a27d7d7f57265
shared-secret-128 e7e84fa13b2c443d0a5c3ea60cf87e772c7faf9f18ef5a0d77113dd6c92c8d17
shared-secret-129 6c7b3006dccdb0b021f00307200793374419358b1367800ace76d988b10ff699
shared-secret-130 b522e81a22720c57040f2e02781728577cb5603ea280be7a1c84750e33d7c963
shared-secret-131 f9ee8b2b668e2a76ed66dfc57006b6403ef8e388c066a0b0493ee0425f8dbe3e
shared-secret-132 1b78098993807e9df54800e2c0f836ea1c4604f7b3c9401311784f69ac4657bb
shared-secret-133 7ee2972f263079a49b5bfc664bcf42c2573af3514340e8e726e08881aa2d33b1
shared-secret-134 7b5020080ce9bd76083ff132dc28ea996314be1a4ac21f987fbbe859f902fab1
shared-secret-135 6d03fef6b354840f4642fba1c8fce9f7d1cda8ae27b6ca01510fb44573fdaffb
shared-secret-136 98009cee0e8ea4fb42dfb70a5ada46ece32e2885a4f9c3a6afba13e82f87e113
shared-secret-137 e8f0e427cc30ae50197d2bbf3a451667a470797d5abe55ff1b1d6de88e41372c
shared-secret-138 b485f3364354b738dc5402729b880c90c502d9ab52ccf81e3d21602c7b9bd909
shared-secret-139 e5781ddaf18dcfc33f4879a2660b7a32a599fc32ea5a1beb28a48caa3e6ec7b7
shared-secret-140 b5a0ec92aecc3010d27d2263d3da66e3d2f3395d23947024a3f4744454622027
shared-secret-141 8462e9df7960ece33089e5a98d61da4faaf3660f93d75f7cc5e397b64cd1591d
shared-secret-142 d44d05d246e61892c1056e28865f60574dbeb6be62680776a537b0b2c3e6b4dc
shared-secret-143 0550ab2f9500b8580dad07073506da9b2db71bd974a85f81dfd148e260bc8ec1
shared-secret-144 1312f05b3e5cae60f27c9485cfd00584cce3f961fee2fdf103c32a79ba17ffbb
shared-secret-145 be4792922519717e235366d64709c51b027d440c6fb8c216fbd92498aaf71277
shared-secret-146 05a2ed58207e0d6a99f37a6da640f8b3b1acaf0c16826bc4fd1eb1e2a6a1534c
```

<!-- test: ecdh.shared-secret-3 -->
`EcdhP256.sharedSecret` against the canra ecdh-p256 vectors: NIST CAVS, Wycheproof and constructed refusals. Vectors shared-secret-147 to shared-secret-219 (73).
```maxon
function digit(symbol Byte) returns Byte
	if symbol >= 0x61 'letter'
		return symbol - 0x57
	end 'letter'

	return symbol - 0x30
end 'digit'

function fromHex(hex String) returns ByteArray
	var bytes = ByteArray.create()
	var high = 0 as Byte
	var isHigh = true

	for symbol in hex.bytes() 'eachSymbol'
		if isHigh 'first'
			high = digit(symbol)
		end 'first' else 'second'
			bytes.push(high * 16 + digit(symbol))
		end 'second'

		isHigh = not isHigh
	end 'eachSymbol'

	return bytes
end 'fromHex'

function toHex(bytes ByteArray) returns String
	var text = ""

	for byte in bytes 'eachByte'
		text.append("{byte:02x}")
	end 'eachByte'

	return text
end 'toHex'

function errorName(failure EcdhError) returns String
	return match failure 'name'
		invalidPrivateKey gives "invalidPrivateKey"
		invalidPublicKey gives "invalidPublicKey"
	end 'name'
end 'errorName'

function report(id String, privateKeyHex String, peerPublicKeyHex String)
	if let secret = try EcdhP256.sharedSecret(fromHex(privateKeyHex), peerPublicKey: fromHex(peerPublicKeyHex)) 'agreed'
		print("{id} {toHex(secret)}\n")
	end 'agreed' else (failure) 'refused'
		print("{id} {errorName(failure)}\n")
	end 'refused'
end 'report'

function main() returns ExitCode
	report("shared-secret-147", privateKeyHex: "e461c5b5e63d75b4c8c123bf8b9cd45e712af08f7e2e494a8f255ac9d80e058b", peerPublicKeyHex: "043107892f3427842c8d6aa60afb1d3b1441e28a67f10e1198a07265125bbda7e2ca9d6df758904f2207cddb862806af0b8cb66ad72b161b4dc959acf614fc2f90")
	report("shared-secret-148", privateKeyHex: "e461c5b5e63d75b4c8c123bf8b9cd45e712af08f7e2e494a8f255ac9d80e058b", peerPublicKeyHex: "0409e5c414b7f5073858553b8b8b4295488e3f3ab966581f240a6124251512270f8fc12563fb9bd6f97b445417cfb14cf9944bac1a26c176b532b698ea8c1bed6f")
	report("shared-secret-149", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "04c1f319279a7bae9f3dc527c42308a7a6255763cccd6f24d266e75f88eb4d022f6b3a3cf14ed3b5e6915ff322b705b175da07223cff4e9ad4520dec7028e1e396")
	report("shared-secret-150", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "044753ee2e3ac201e9a4bf25e31513a45365b5d0526dcb2e0a88736fc8ae2681c1cc49b1e868d4de224593ec7e00e9f2c209f3b935428d3bfa978f74869af889e2")
	report("shared-secret-151", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "0429c1e774c9d3a8930df8a663fadc8b9a6092d8054ff37adf598edd78d0a3ffc57173a3594be68b7ad191e0ff27d79eb4e7bb22f1090873cf307cd073b27368fc")
	report("shared-secret-152", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "04893cb0c308aaa2092d9af93e0f02cd951ec386e866480eece443cc89e672733ff6c6f43caa1e1d168ba3ead9ea041cf4f227d76b90e9e25f497f2ece8935512b")
	report("shared-secret-153", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "04c5a2dc340e0983aad4f895bf54be9d9e176d6ba1ba6c859ca0851889a97b7dc5c90dab79207b861fd996429639cd8f31be0298b63f4934923a483a9e6f7c5ab9")
	report("shared-secret-154", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "04e9db2cf8037505fd8a8044f4b3f80f67f9688815c69c788ddb26fcb3c84cb8adf02e121e4020424c6d8f1d269c00f2a7f23a59237fb48a5ba17ee57c1c264282")
	report("shared-secret-155", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "04776aef1acb82b628e132cc29440988f0a15d4cc2b4f328aecb063c9b86e5018e91bb2038fbbb05573b1c943de8bae0853d6a934d4d164429aa145d68e9c2e0cb")
	report("shared-secret-156", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "049ec06b0b08662c0e1dd9111696a63a1601cc83cee20695778adf84d43064fc90ea9ffe0e7b32c3e30e5f7809d9acc49a8da7b77742c2a3d3660f1cee1dd4be19")
	report("shared-secret-157", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "04aa42c977da1ed42e52041c0af2c090f244b22ae354cc9b10781e44c89eebe336f7100a0eac8b55513ce57a8d0c195adcf50d1b05fd35b0576cd92805cbab4c1f")
	report("shared-secret-158", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "040730d77d9114d5610d49a98dc196ca9e1e8fd2abe007da96b574f736bf1939258c8814055a774f7e79f7b4fb9cfb03d839566afc6059311f3d55d49ca4ddab31")
	report("shared-secret-159", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "0485a82fabb60c7664e2500c59d0f24eac1aab0060c07b49828c55009fd27aa49647b85f72b467bfa2fe1b13258feb121aee75a3155b30e8b4b566b610f13b6dc5")
	report("shared-secret-160", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "04833f2a6e5c76c3ddd13a106cee4aa8b2a9dc32e0bd7bbe2a6322abe0ab4110a2ab93845403808a7cc8e8b65fa42da414327ec3a19d539fc033ba8d8aae7cb23f")
	report("shared-secret-161", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "04cf8116ba0901b0b95308bc686fed90ceba0e5d424c8ec206cab8d7cffe93fba2d5e7a62cdeab84a20fce37b5b0ac168ca2a85a1d18cb04ee48deb250b54691aa")
	report("shared-secret-162", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "04e754cdf8e56944d5bd36d025b1c0f5fb2754a2eee0fb5200bd169cdfc83f07c38f2b7881de647c2c66294e1adf767e515adda742d3a863f0cdd6665b964e60a3")
	report("shared-secret-163", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "0474f1feb9a641fd87a49e16fbb1be363297ef971213bc7d36b6abbf29d0e88ac34b6c246b313be6c3c9f33defd4ade947af547168f7c22278df8cb7b06a8018c5")
	report("shared-secret-164", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "049368066a0748867a7b870244f5c9f82ea8bd51552959dd550bb7394497159a5dbf89b521e51db372c0bcd11fee41682cecf8e702f5956f1274efee4dfcb2f65f")
	report("shared-secret-165", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "0442e831d5dfd2b9f5387263cd686ee03dfa235cbc07f6f3a87a7e8150556362b32be47f8e3011572855ca487ffb594f7722ef9fc3292902a23146c19bbf6da76a")
	report("shared-secret-166", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "041321838efd80e1dd8e4d0d95d17895c7dc60303a5c234182b592d9d21ef14752a44eaf3394c63708763f3b97b2d2863fd3b77ebda44862c7c23daaa437d5fe59")
	report("shared-secret-167", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "042407bb2cef161472cc799db14e703183676f312c571e9f3df137f55df9442c4077bf00500aa7bece8c1f762ff08a0ec71bfdadfaf13380704e6202de62c5f22f")
	report("shared-secret-168", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "042477b9ec12029a86f50cd95dda6cead7a34f1c2360f39e3d1159793c8ee663a6bcfed85bd19d4ed3855807267e39156413b41e39a701d8d17a23ac685fe5360c")
	report("shared-secret-169", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "04c829d3a99bb47e4256fcc63210c13282ab59e7aae90877b1797bd699d672a38b8696a03730704204894aa967273c120b57cbb71f9fca62c64f692e7f49598d2b")
	report("shared-secret-170", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "04fd61ec5552fbb24b575ebdc9d4a443e156bfc0d13c9dc611149e21d79c48c444b7b73a8dbb83a5e2267a8bcd398b10af0843474c750265e9d48d04ac4ab60405")
	report("shared-secret-171", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "049cd9e0f17c11a10f8aaa1f47ea7141f6399f40b079b07b14cd388f3b6b0a47a8efc86998f717a83e53c8e58ad23c71d0ecc8bc841bc16ff232a5e326844cd4c9")
	report("shared-secret-172", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "043846b407782243fdf3b04042990b16e2abdb325ba098532879d14e16e914c274de571bc4ce2edd7d2d37df5593959b5c7ab59ac8fe5e06c44bbae6960b6c2510")
	report("shared-secret-173", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "04d3a07a4b6f9b660d92b3fe937e2ebf91dda8005a7ffd0292dad91d0592f1a89ed7209513bde00287b8234e280c00b3c59dc5e333c8c56864bb6cfb364e3f2d85")
	report("shared-secret-174", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "042ad9550728f65b142ed676438bafe80e365cff4eb557526560e4adfd696da778f0f01da8e947628346614ed7fefe24901f9eee79de6dc06d85d563722ec77961")
	report("shared-secret-175", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "0419f5f0b4edd6124e340adbcce791b06d14054c7eb1f9914b44245c51d6c1101d82304d117f729ef06d97eb56aeb99a6222dc3de5e30c071956fd73ee8cdbeb81")
	report("shared-secret-176", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "04884cf0d17df4ef5e488d725b47d069cd2b99940b05b26efcd55d0e5ecd205b61e3b191a7f9f920cfab05b0f9cddd02fecf857d0e405ca36f0106aa367d492460")
	report("shared-secret-177", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "04b1e5aec6272dbb856417e7ce6102bace3e0dd7c32386511e065c1a32d5688fc5c078aff94b8eac78bdb54b210bc74676d2336ab7bc7236808291540f65c82982")
	report("shared-secret-178", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "04ff8d38f61f633c5eb570ef877e61b4466b511ecc3dc3de87e6224a8c78c0893d7153d4f87b7f9d0488b6588df56c9934ae75d1486c1ebdb49444bad832d08017")
	report("shared-secret-179", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "044c381ef0302e1b428f4887f7b4fde4960245920f40c4005669a091644c27482bef739bc3a3d22c6e707557f2469f504aea9e8a07591776b3324beb5c498e6f55")
	report("shared-secret-180", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "04245a530c56d07978f91177e8d97cb1368546bafefbf8148cef339fe7bb652ba5b27a60edd2f498ca636996a9e5e629d5c9ec203217c1b5402ff99a0d2fa97cad")
	report("shared-secret-181", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "043cc566a230b3df272a172cd80fb481cfdb8dd1c27a84dbb7a391f11484f14ef99be9d4cfd33748ee157625f809d0fbc1a6f0317d97dbfeda4999742455fdf78e")
	report("shared-secret-182", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "04455fc13990663bf24cf5908c59e4c3a084fc39ee1a7b14d812c227a8dabd6f94685d00ad604b206d59b2f4d9a75442083b85a316dab78574a3bbaa6259e89a3e")
	report("shared-secret-183", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "04f933578fd1938e44b271ae431ffd0df5548496945d91cb8d311ebec9dd50d036f3d88fa2aae4236cb08858158d725e1213fdb982503bc910a5b58366cd85ee0e")
	report("shared-secret-184", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "045606cbf0c5b52f0f464137d0133559fea50af271a9826ff6125746636d981850d7b10dab8e64b2f8df6fafb8d5943e7b37c6500fa95bfd6680d8f2ff74c4407a")
	report("shared-secret-185", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "04bf236450ef33bceffbb90e0b86f8e99deaa1337c89826363b3cb4e0a66d541030676703afc892b8059c92e24f653b9cb2a11a5f520c848303c17c2c48600c84c")
	report("shared-secret-186", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "04000000000000000000000000000000000000000000000000000000000000000066485c780e2f83d72433bd5d84a06bb6541c2af31dae871728bf856a174f93f4")
	report("shared-secret-187", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "0481bfb55b010b1bdf08b8d9d8590087aa278e28febff3b05632eeff09011c55798cd2f199d9815d7585073034eb76c93d50799b354b0fb1e77eb75eba8bff3d58")
	report("shared-secret-188", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "04614dcfbea4789a3f3eb4a8e2f111c887f0248d9316b99d0864c927a045d6941753a073befe08491a8050a4d96d08ba4790ae18db3ef7f0eaccf59ce1095afc54")
	report("shared-secret-189", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "04dbfa466f12013255f9d57a6496c158ee7dd202a1ce4a5a53005b3564d509a0bbf2578007e857bdd082751ef2f3b4b9c38a0b87bab413d55ccb26a574f2b4be9d")
	report("shared-secret-190", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "048238f048c292e08d49bdb5dab1f08da6d39cdbefb1d75e03e76048b903d6baf9389e4da4040fbe2019a1461fec3cc90403c9becccaa02ef5793da721c1e657a8")
	report("shared-secret-191", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "042d08cf1230463b7102e8a54185986a62a3f2520c0c151ace12f5d073a40cbb2f13473d11f58dde7b16783083cd533bdecc092249dcf15b723f08394a5bc5b3f0")
	report("shared-secret-192", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "046e8966a6013dde2bb3b2a28c6eb013c481ddb7249ef19d73b92da35cad1d6f14604bf43d798ae27621df2eddc8f5d90ee0289c54bbac82b3c5264c478761d153")
	report("shared-secret-193", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "044f612e0252b8d484bd2657a9ca1bba22afe978466aae6f12bf1d9171e01683ca48f4f3c986bf941b523889a7583fae695d3d4ea4adfb57d0c82370fd82364c1e")
	report("shared-secret-194", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "04e8b782dab2dba4db066670ae656c7eca4abc7b1a24c7976de2ce02f25b668413bda2f9991ad7aaf44437bfdd3d91c8f94178cde0f8af9833544ccc9e2d606b8e")
	report("shared-secret-195", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "041350cac374bb076ee89eb8af43d076afc7312021006431fd909d22df064f6637dbb0dc949a183e398709353d264b13c4266653e8a8685dd830ffaad2955ae9c5")
	report("shared-secret-196", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "04ec624cda95af6633088f9809e441104322ff529e0952aad4efe3286203e0c700396b89e7168b1da4dab123aa6eb7ed60069d07b241d6fff4ba04647e4fccf1a9")
	report("shared-secret-197", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "0441e9d4cfa8efe80b895a8cbcce2568e251db7ecdfd20a7ad710d4a4bf2addc6b5ec36a8339168a03f15b8c80f2a2a828f151d38791584853ba2ff44a2a0460a1")
	report("shared-secret-198", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "04776aef1acb82b628e132cc29440988f0a15d4cc2b4f328aecb063c9b86e5018e6e44dfc60444faa9c4e36bc217451f7ac2956cb3b2e9bbd655eba297163d1f34")
	report("shared-secret-199", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "049ec06b0b08662c0e1dd9111696a63a1601cc83cee20695778adf84d43064fc90156001f084cd3c1df1a087f626533b6572584889bd3d5c2c99f0e311e22b41e6")
	report("shared-secret-200", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "04fa51d128adc2000f09ff12c6fd8e25aa08556d708bf6b0ffff9e8eaad4783f0de22bf529e516e1f64b8e0d09f98fad4e501695a930a1b22076659da707e3ccd0")
	report("shared-secret-201", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "04efe7754ed4c0b3c1dd301bc1ed69800aa2ff5d51fb85937715e60d2e7bcada8eb1581ab75fb3c797ef94a9dba3d82568c84617eaf3fa04f279fbfd898f704604")
	report("shared-secret-202", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "04d8e13fbd017f1f9a26be35c611d7b2299f5d10de3c8a26362273fffb85238f3ed1426b748c1f87e3afa2c1e7a0224310c980655e07399590d1494d6d6bea0396")
	report("shared-secret-203", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "045a1027666a0e372481fec0b3901e058d60107c07b1115550ceb05789b55a6d35063d4c8ee66ed45ff3e1dfdcfd73ed96a9e83193884adbcaa574b2dd118a692b")
	report("shared-secret-204", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "047937b9c40986dd755a0656203089782583da7d8113a44190762ab474a20bcf60efcbc1525aed5b4ad8e687cb02c2ef8887095cadca56c765b41b4a9544ff2fe8")
	report("shared-secret-205", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "049368066a0748867a7b870244f5c9f82ea8bd51552959dd550bb7394497159a5d40764add1ae24c8e3f432ee011be97d3130718fe0a6a90ed8b1011b2034d09a0")
	report("shared-secret-206", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "04981d7449bdf0013f5eeddbb7e42c442f7ccdd9427bd26d7b388755aa5e26f46a1292b88fa6bf5dffca054dd42ed3594277b593dcc402d80340fb7816e4dcab37")
	report("shared-secret-207", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "0409e78d4ef60d05f750f6636209092bc43cbdd6b47e11a9de20a9feb2a50bb96c0000000000000000000000000000000000000000000000000000000000000001")
	report("shared-secret-208", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "045384d6c0def78960db967b8096d35477c5a5ce30ef0c6d8879a5568ca87e979401ee56c4581722610b43f3cbfcf3862c082a6e36baa36fd6f78403c0e399faa5")
	report("shared-secret-209", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "044eca7641a4afd5eab0b214657ff3bdcbfc66f1551a53bb59493bc38ed78ff39614a0cadff14c14736edbdcdab510cba07a8924ffd0490ee514aedfaadb648b01")
	report("shared-secret-210", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "048d0177ebab9c6e9e10db6dd095dbac0d6375e8a97b70f611875d877f0069d2c70000000000000000000000000000000000000000000000000000000000000001")
	report("shared-secret-211", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "045fdb7f0cffb8b5b1142d24698a4bda76bf9827d63b1a6bd85a4e2f9b59c510cfbcb35ba9c987108b6d4337ad5393f9f910ec92410c230869d66528ed88c1b98a")
	report("shared-secret-212", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "04530b2293e60c6b6f14c75c90b1ef8b9f9fa6b2151b8d9855792eb2b3dc69f07a0db42440e73fd7d6df04aed5022fbe21ceaec33c5fbade1bd6ad321ef2e10d0b")
	report("shared-secret-213", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "046916fac45e568b6b9e2e2ecd611b282e5fcc40a3067d601057f879ce5a8a73cc0000000000000000000000000000000000000000000000000000000000000001")
	report("shared-secret-214", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "04ed9568c85bc52a6b45733618c3602107c1fdacf23b1a38e486af95978a214e2efa0d71d5e737891c4276e247581ee6139011ca1460db9b1e20b364d9275683e2")
	report("shared-secret-215", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "048270f8179d57436b34dfc0bdf7d417a5c895116b90cb51aec718614f864a635d174804e0c0e06e3d68d3149e0b956621c6aa2bde83f4d17d03d28ef8aa389fff")
	report("shared-secret-216", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "04c61750e98abaf20225a881dbfd3510532cfc3df971bbbca4a2bd52f91acc9c59d0fe79342097f88ae78fc79a8032245fdd2c30cc64aceaaa9fd57b0825692531")
	report("shared-secret-217", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "049c5d3bb54650d9550e1ee2efa3ea43c14ab99d18bb049f37b42a6dac48232f0bd3a2760d83d33afe4ce6f1d1245489c509bd26b0251f308f8c996e80f7a3f8eb")
	report("shared-secret-218", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "04f1724efd481ad45a55795f06126b1f5ed28e7d9bb4fee910af2ad8c1373b18ff77edbc34da6c787ec73430347f4da86810032d88f7475f6c42f15914079d179e")
	report("shared-secret-219", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "04fc3680af52fa89ffcd193ecc0b0714466fe5db277ee5872846c520bf4e3721d927260a0e225a3d377e6723ecb6bef8d4493c2da78a22a307fcca8f88f4527208")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
shared-secret-147 def48de01d86d7260e5c2a6439068542437a20b140dcd45623dd1ec0d1394742
shared-secret-148 c06f7373e67ee8c64dd86127fff8b2d23d284420d30e571a79fbb9b54aa70dfe
shared-secret-149 32dda9876dcac25c58543304a2cf6c1b31fdabf3c89e43d6eee628a02422b19c
shared-secret-150 4c54d63b14b8ff10b653f663f3230c1827d682c833ead1f44ad6ae8e7df35e2d
shared-secret-151 4df3a98474884b1c3c78b6c81e4f90d53d8bb4daba4227c411a5747e6aac8cb8
shared-secret-152 d2aa087051fbdc35051d21ff415f934ab5698629abde5d4b39f2f6a76e3992cf
shared-secret-153 ac7c60651653a4f3d4478ebb9aec4381183ffe1b152b6643c07c47c0b2a489cd
shared-secret-154 509d06dc43813dba9114c13c337bdc0bccc2c7dfa7a7d8960e5593c52849c855
shared-secret-155 28a88b6b258f233020ba6fa9c00d1d72831f4515b86966a9782f521315e18aa7
shared-secret-156 c4ff865ff3dc4953ea78d92a02f3345a53bdb6050cfd8f41baa4395ecb6acab8
shared-secret-157 98afbf6e00eb004c79657b40b7105f8526b5205942af2218f453bc26337306be
shared-secret-158 c537409021ad95646fbccf9232ea47fa764d12db7595f436a65ad5b13cdd19f8
shared-secret-159 ad60146b1cf2d9661884b9ef867cbb09e42466563936b03174efd8517ca8c6e0
shared-secret-160 f677a03d68570cf6083a1ba415b40b5f7ffdace4eeb804983cbd03b979d939b2
shared-secret-161 30c9fb67a3db93a4b3a54135d265171a14a2a0a79cd715e13515510793d8d081
shared-secret-162 8451f3414247058c32b53be128cffd2f2887434ab902b67b05fbbe7aeed8cbd1
shared-secret-163 dd6bf055a9e431479707e64590fc58f97ad3238d063ce8f66d5d37b2e3475638
shared-secret-164 4529f4b631c9984ab216a6801281fc4fd8731a58b65ca8d07bff07811116371f
shared-secret-165 e6d3991eecb1a4b78aa700ea56934a38785b3662c59766e2ee5b157f77ecbb7c
shared-secret-166 99bfe73eaba2599e79307488047236de003e9fd26f5ff1dbef62b3fec75fd612
shared-secret-167 faedeb03f8b5178db58604ab69b10b176e92c5deccf4249a9d60010dcd16a635
shared-secret-168 47e5858af99e4e916054897807d685c8cb8b0f1692ba782bec2290a58248c683
shared-secret-169 ade4ff3437df254696fb2b19f5154108d88e775e57d345bbf57a02b2abb3f07a
shared-secret-170 598922a353fe1add65ac8f66fd225e826fa8ce493470322593735fc6627e6f2f
shared-secret-171 923cca706deb11de7c17f8dc089dadcee15042d5619732d9ea537f7dd993fc31
shared-secret-172 bf6234cf4de2040548dd0ea2a99310389db48fe6c0eb81395ab2d42484f841ae
shared-secret-173 da223bd733d403e2f2f9f59ff712230b0ab7feafc87dc7021644569e3d513795
shared-secret-174 fb888df1c34b3ff15620b94a20726954eb7989e6f8a266ee4902f45e7e86e21a
shared-secret-175 c656a27fc48053f9bcfa3a240f904be76954cc8b8cb3f4790613e618c5069ddb
shared-secret-176 d990ca49c9b414818622b9c69197fccb197c94bd1ce13c24110e94c5c321e2c9
shared-secret-177 973af53e8a92e7469856265229acbbe0adb5574beb32ea6780be385b28ae8b07
shared-secret-178 d9fdde34e48cadf6e63c1de84b22da7180ac644c42c055d28625b2bf11cf4951
shared-secret-179 9dda17a91ad44d472304c805b350f9ee55de2d94d7327160dbc50e2eaa85e4e8
shared-secret-180 7a69b8de61da48f9901ff440192fc90086c3a2bb56bdb224a55b89cb82c72162
shared-secret-181 76a4006db3de3e098936d004cac1f7af41d515f753b9aa4433eb2466da42acfc
shared-secret-182 3be92a857ffb42c5d5c128925b89aec34cfe649920dfd647f0a03f421fb20f09
shared-secret-183 19e0d41257ccbee1c453f834fa5f349cd9bbb98a92e848354a7579dfd2c78e30
shared-secret-184 3a7a54996053b41361b605e73852a5a0b17d85b42b3c4a6f119c6c87ebeb9f03
shared-secret-185 72f042a5b276a980d50a0583483eeda72de8ac796b1d50a843b4552a151e199d
shared-secret-186 40eafb850ca7e08f19ee6447f196aeabf292e27397856e106e60a6c8b48cdb27
shared-secret-187 b485f3364354b738dc5402729b880c90c502d9ab52ccf81e3d21602c7b9bd909
shared-secret-188 4207bf4159faa0e50ed238b9c0ff46194a539a1ba03a5a4c8d68f369aecd31a5
shared-secret-189 e2d57eeec983756c9124f885a4d118ed5b8de7d2895fd91264cf291496949a12
shared-secret-190 81fa8ca50ac155200de410dfdfd8093037674baba7ca102828a5d0026f24f0f5
shared-secret-191 151411c3b8e6d25c515186c1d9b9c6e1a05da5a7f09cd4ea8844e0916846ba48
shared-secret-192 5460bbc867f4f61b79ed491e41779e2ac8dea27ec1320b558f3cff5b6a94c224
shared-secret-193 a78ac266fc5a85102db78180bd9ae4b77039ec97391a211403322e4d56b59049
shared-secret-194 f481ae23b9192191e8e1c47203f6a807e54e1ff471e6642716f07895b32f4451
shared-secret-195 1de1656d961de1f584a2f7179bd3be3b4a282cbd3248f29e74b73a812cad8d90
shared-secret-196 50b13920c2368b9a2e8c80a02a6f4e4a2f84af33532eaf5728da161e620a312a
shared-secret-197 b48e119d29eef7dbb76b64218e728ddbf6ec600505ec7ced6ab6fb8763308da5
shared-secret-198 28a88b6b258f233020ba6fa9c00d1d72831f4515b86966a9782f521315e18aa7
shared-secret-199 c4ff865ff3dc4953ea78d92a02f3345a53bdb6050cfd8f41baa4395ecb6acab8
shared-secret-200 de1069f051637e10166559cef44688afc809341855261215c4f381d9d7da76ca
shared-secret-201 b5a0ec92aecc3010d27d2263d3da66e3d2f3395d23947024a3f4744454622027
shared-secret-202 d2a5bc66498c6036aecdfaad041cef732a893de190a0a5b42ff71e13f09280e7
shared-secret-203 1f812313ddcf36bc38071d0e51a74100d630c8e20cc414326eefa42ecb1b5f8e
shared-secret-204 f284089bddd5e2e1be3f82640efa0658468fa1f10b281963a3ca190c3982fda6
shared-secret-205 4529f4b631c9984ab216a6801281fc4fd8731a58b65ca8d07bff07811116371f
shared-secret-206 64bbc9fdd73643eb2954f4ab640381b938c5e601846a0c6b6954966e0dc73e6f
shared-secret-207 28f67757acc28b1684ba76ffd534aed42d45b8b3f10b82a5699416eff7199a74
shared-secret-208 9ee653cda46db67612760ce35bac8450bbf48dbf74451ed93abb6db408a9fe10
shared-secret-209 9736ad6b2a2ef17ec3f8c8dc2e35715fb1c06f28d82e4e26876f0214588165f1
shared-secret-210 748fa4f5a399320382dc920026938694c41a26fe2aaa318c5e710198dd71c793
shared-secret-211 7f97db83b4d86f04fe286041ee21e80ec3d59f3ce82cdeeaf362016fc87a3e02
shared-secret-212 21794cf24f56273fa4463cc7ae4232fa34dbe0f18b73613b8ae9cbfb9c36abf0
shared-secret-213 915106d07816e879e7643f00abf6d79fb8f1cb78bf64a6a3827f91a7b0ef0f41
shared-secret-214 2fcce552310819dd775ab7ba9ff0f96a1fcadd25a0c709703cef04bb6e1a7bd7
shared-secret-215 3db29ec6f978d2269e92e9c7eb5c8b5a8e56c2228a4fb9e483feca50aa3e451f
shared-secret-216 72c57c2e10d77318b3a796097bbf768c6366142d80f98c90a93780a841075f32
shared-secret-217 a96b07944e9eb2b22a9a36575eff1f4f6363b4aa3a53b100b8518a67ba5405dd
shared-secret-218 855883316b6d097ae5eab6c67e8411a1397349a09b9d7d8f096b2ba1bd03ea31
shared-secret-219 5a75bb7a0c96b8340d0842bcccf11974e1a5a2c8f4bc22b333433cce646b6a8a
```

<!-- test: ecdh.shared-secret-4 -->
`EcdhP256.sharedSecret` against the canra ecdh-p256 vectors: NIST CAVS, Wycheproof and constructed refusals. Vectors shared-secret-220 to shared-secret-err-037 (71).
```maxon
function digit(symbol Byte) returns Byte
	if symbol >= 0x61 'letter'
		return symbol - 0x57
	end 'letter'

	return symbol - 0x30
end 'digit'

function fromHex(hex String) returns ByteArray
	var bytes = ByteArray.create()
	var high = 0 as Byte
	var isHigh = true

	for symbol in hex.bytes() 'eachSymbol'
		if isHigh 'first'
			high = digit(symbol)
		end 'first' else 'second'
			bytes.push(high * 16 + digit(symbol))
		end 'second'

		isHigh = not isHigh
	end 'eachSymbol'

	return bytes
end 'fromHex'

function toHex(bytes ByteArray) returns String
	var text = ""

	for byte in bytes 'eachByte'
		text.append("{byte:02x}")
	end 'eachByte'

	return text
end 'toHex'

function errorName(failure EcdhError) returns String
	return match failure 'name'
		invalidPrivateKey gives "invalidPrivateKey"
		invalidPublicKey gives "invalidPublicKey"
	end 'name'
end 'errorName'

function report(id String, privateKeyHex String, peerPublicKeyHex String)
	if let secret = try EcdhP256.sharedSecret(fromHex(privateKeyHex), peerPublicKey: fromHex(peerPublicKeyHex)) 'agreed'
		print("{id} {toHex(secret)}\n")
	end 'agreed' else (failure) 'refused'
		print("{id} {errorName(failure)}\n")
	end 'refused'
end 'report'

function main() returns ExitCode
	report("shared-secret-220", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "04106b6f81e3482db18d74029291821ae448c38844ef783bf1d6999a404401f63f6a5753f0edc68a62cfd6a0b181bb2599e1f3bac5fa8824af160de79ed867c350")
	report("shared-secret-221", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "04093cb5193a4f94cd18edaa20a973b87ff79b0c03684c79487ecfee347e5354eb04fcb5752539170777932be15cd84c97f03815ffee8b60b647c178eebb8e14d4")
	report("shared-secret-222", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "04d6c38f448b964e27b5b450cc38d3cf41ef9df83d8a959771eb9c21855cb36445df638aef46a2aeb13199281e1a26d12fe61b029ec7f68b90faa89f88c7a95942")
	report("shared-secret-223", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "049a4487fcfce8396688e7449e095fe803caa253d4bd7c66dbc6261cc9d9f883a50e5251bae29c5a5cdfa31bc61105671a88a018467398158d35b88829237c0bff")
	report("shared-secret-224", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "04fed6ce127290c1291ca5ce64acb4e0f2f8905654d1d25ba57c1f74ab52f21f42963d31671c06b802169929525c4a1fdeff5b1eafab919dc2df6c52be84dfaef3")
	report("shared-secret-225", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "04f7cee5b55f1869f137dd707c8f8fb8965a2be5840c3149fb759695a4661b9c0d23c78c4e9647b0d6cb2f2602be73ff25cf3d09c96d892b5745fe5eca814aec91")
	report("shared-secret-226", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "042baaaec3b3e8d54a4e18f0960b947da2535e3cfcca2cfa8b7113aad8e3b6626f72f71e7c9e96042c1d39cc8f1139d5147c6f4fe62e23cf6df364b5f4d899f842")
	report("shared-secret-227", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "04a51ab1238bc1bed25247e7d179c83a61ae2d4a9fe2288c363ae0eb7a77de432a3c6d35d82ba8017e6ca9041cc785a30703f7bc4427506e624ac5979d715421dd")
	report("shared-secret-228", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "048b5ae8a0e55f30f509061315abae79ac480f88b44655f7269a385c81526884be262974a31a0e2322126c2d77b26b108abd81f8b952c458ccc95d46fb4924c7c0")
	report("shared-secret-229", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "045f60c77e474dd66c8135ee3dafc75ba644649824c72737542091ad469adbb685312c09c69b629d0436bf3bd6c6083ff2a87be484a73ef3a5d2c3e06b5d9b21b3")
	report("shared-secret-230", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "04e06eaa73f6feae45417d859bbad4bc404b2885bcd213ebace594e16f4970e0c411ed3323a3d7afc7076239884307f91849ed5f5e36b6171d309c81344c53e06d")
	report("shared-secret-231", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "04341592390ccce485de8880f3d727f664c381914a1becec383b35586751fc81c2add71852b87016e1019cae7a9080e75ce0b0b8aac175d692d5e7b4dad088f5cc")
	report("shared-secret-232", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "04fa764b6b76a86c3b762120825d353a24766208c1f5cc0fe3fe7998026a2ec5c43bb2f948fd94cdaa5869b1e0e73a4d97035cc49357fb7b74d7ed0a2c5b8d54eb")
	report("shared-secret-233", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "04a71fbb617199bd585b4b66212ca33ca9e09370e6bf15c8ea0acefd9c8e945d06840f058863078e743e220ff99f23bbc1daa36835d4b1269f0a7536e63f06d853")
	report("shared-secret-234", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "0413c8292d854d39451c0c63a802b8c03e4fcb875ef01239896295ba1c0f386975f82df197086fd86032cb36b69a27876dd75a8e9679f36ffc2210edb128d4be13")
	report("shared-secret-235", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "040cd9df415acc0c32fd4e3d6924ce53075b0452bf919a2ab2ebe26597570f1ecd5985d8d2c5df78fc100f87efb6dfa9543757bdffecf083dfcd1ecb38de6c23f8")
	report("shared-secret-236", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "04d2dbea4046b23fd2b233d1ce31dceddb89b25f26c0627a9d2db3c5605c9cc99535bdc8de7451c1e27e97aa91402cce3882c71269d9cbdcb5d7ac0ceb911b9b6d")
	report("shared-secret-237", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "04888fb044fb2b6caa60366bfa662adba479b8365a6555a29887d580f587086ba8482f4ec24082a48d6402afa1622143f26e61d91b7e30d6a4b223630ee10f70fb")
	report("shared-secret-238", privateKeyHex: "809c461d8b39163537ff8f5ef5b977e4cdb980e70e38a7ee0b37cc876729e9ff", peerPublicKeyHex: "042e2bec134249379d57700301f3a58e4b395a4d28370d2a06e65e7ac89ed76ac697dc960bd795cdf4fbcfdd75149057b8e022331c7b5461f383ac589d764df333")
	report("shared-secret-239", privateKeyHex: "0000000000000000000000000000000000000000000000000000000000000003", peerPublicKeyHex: "0431028f3377fc8f2b1967edaab90213acad0da9f50897f08f57537f78f116744743a1930189363bbde2ac4cbd1649cdc6f451add71dd2f16a8a867f2b17caa16b")
	report("shared-secret-240", privateKeyHex: "00000000ffffffffffffffffffffffffffffffffffffffffffffffffffffffff", peerPublicKeyHex: "0431028f3377fc8f2b1967edaab90213acad0da9f50897f08f57537f78f116744743a1930189363bbde2ac4cbd1649cdc6f451add71dd2f16a8a867f2b17caa16b")
	report("shared-secret-241", privateKeyHex: "0100000000000000000000000000000000000000000000000000000000000000", peerPublicKeyHex: "0431028f3377fc8f2b1967edaab90213acad0da9f50897f08f57537f78f116744743a1930189363bbde2ac4cbd1649cdc6f451add71dd2f16a8a867f2b17caa16b")
	report("shared-secret-242", privateKeyHex: "7fffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff", peerPublicKeyHex: "0431028f3377fc8f2b1967edaab90213acad0da9f50897f08f57537f78f116744743a1930189363bbde2ac4cbd1649cdc6f451add71dd2f16a8a867f2b17caa16b")
	report("shared-secret-243", privateKeyHex: "8000000000000000000000000000000000000000000000000000000000000000", peerPublicKeyHex: "0431028f3377fc8f2b1967edaab90213acad0da9f50897f08f57537f78f116744743a1930189363bbde2ac4cbd1649cdc6f451add71dd2f16a8a867f2b17caa16b")
	report("shared-secret-244", privateKeyHex: "ffffffff00000000ffffffffffffffffbce6faada7179e83f3b9cac2fc632551", peerPublicKeyHex: "0431028f3377fc8f2b1967edaab90213acad0da9f50897f08f57537f78f116744743a1930189363bbde2ac4cbd1649cdc6f451add71dd2f16a8a867f2b17caa16b")
	report("shared-secret-245", privateKeyHex: "ffffffff00000000ffffffffffffffffbce6faada7179e84f3a9cac2fc632551", peerPublicKeyHex: "0431028f3377fc8f2b1967edaab90213acad0da9f50897f08f57537f78f116744743a1930189363bbde2ac4cbd1649cdc6f451add71dd2f16a8a867f2b17caa16b")
	report("shared-secret-246", privateKeyHex: "ffffffff00000000ffffffffffffffffbce6faada7179e84f3b1cac2fc632551", peerPublicKeyHex: "0431028f3377fc8f2b1967edaab90213acad0da9f50897f08f57537f78f116744743a1930189363bbde2ac4cbd1649cdc6f451add71dd2f16a8a867f2b17caa16b")
	report("shared-secret-247", privateKeyHex: "2a265f8bcbdcaf94d58519141e578124cb40d64a501fba9c11847b28965bc737", peerPublicKeyHex: "04023819813ac969847059028ea88a1f30dfbcde03fc791d3a252c6b41211882eaf93e4ae433cc12cf2a43fc0ef26400c0e125508224cdb649380f25479148a4ad")
	report("shared-secret-248", privateKeyHex: "313f72ff9fe811bf573176231b286a3bdb6f1b14e05c40146590727a71c3bccd", peerPublicKeyHex: "04cc11887b2d66cbae8f4d306627192522932146b42f01d3c6f92bd5c8ba739b06a2f08a029cd06b46183085bae9248b0ed15b70280c7ef13a457f5af382426031")
	report("shared-secret-249", privateKeyHex: "0000000000000000000000000000000000000000000000000000000000000001", peerPublicKeyHex: "04700c48f77f56584c5cc632ca65640db91b6bacce3a4df6b42ce7cc838833d287db71e509e3fd9b060ddb20ba5c51dcc5948d46fbf640dfe0441782cab85fa4ac")
	report("shared-secret-250", privateKeyHex: "0000000000000000000000000000000000000000000000000000000000000002", peerPublicKeyHex: "04700c48f77f56584c5cc632ca65640db91b6bacce3a4df6b42ce7cc838833d287db71e509e3fd9b060ddb20ba5c51dcc5948d46fbf640dfe0441782cab85fa4ac")
	report("shared-secret-251", privateKeyHex: "ffffffff00000000ffffffffffffffffbce6faada7179e84f3b9cac2fc632550", peerPublicKeyHex: "04700c48f77f56584c5cc632ca65640db91b6bacce3a4df6b42ce7cc838833d287db71e509e3fd9b060ddb20ba5c51dcc5948d46fbf640dfe0441782cab85fa4ac")
	report("shared-secret-252", privateKeyHex: "ffffffff00000000ffffffffffffffffbce6faada7179e84f3b9cac2fc63254f", peerPublicKeyHex: "04700c48f77f56584c5cc632ca65640db91b6bacce3a4df6b42ce7cc838833d287db71e509e3fd9b060ddb20ba5c51dcc5948d46fbf640dfe0441782cab85fa4ac")
	report("shared-secret-253", privateKeyHex: "7d7dc5f71eb29ddaf80d6214632eeae03d9058af1fb6d22ed80badb62bc1a534", peerPublicKeyHex: "046b17d1f2e12c4247f8bce6e563a440f277037d812deb33a0f4a13945d898c2964fe342e2fe1a7f9b8ee7eb4a7c0f9e162bce33576b315ececbb6406837bf51f5")
	report("shared-secret-err-001", privateKeyHex: "0612465c89a023ab17855b0a6bcebfd3febb53aef84138647b5352e02c10c346", peerPublicKeyHex: "0362d5bd3372af75fe85a040715d0f502428e07046868b0bfdfa61d731afe44f26")
	report("shared-secret-err-002", privateKeyHex: "7e4aa54f714bf01df85c50269bea3a86721f84afe74f7b41ea58abcf3474e88d", peerPublicKeyHex: "0400000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000")
	report("shared-secret-err-003", privateKeyHex: "7e4aa54f714bf01df85c50269bea3a86721f84afe74f7b41ea58abcf3474e88d", peerPublicKeyHex: "0400000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000001")
	report("shared-secret-err-004", privateKeyHex: "7e4aa54f714bf01df85c50269bea3a86721f84afe74f7b41ea58abcf3474e88d", peerPublicKeyHex: "040000000000000000000000000000000000000000000000000000000000000000ffffffff00000001000000000000000000000000fffffffffffffffffffffffe")
	report("shared-secret-err-005", privateKeyHex: "7e4aa54f714bf01df85c50269bea3a86721f84afe74f7b41ea58abcf3474e88d", peerPublicKeyHex: "040000000000000000000000000000000000000000000000000000000000000000ffffffff00000001000000000000000000000000ffffffffffffffffffffffff")
	report("shared-secret-err-006", privateKeyHex: "7e4aa54f714bf01df85c50269bea3a86721f84afe74f7b41ea58abcf3474e88d", peerPublicKeyHex: "0400000000000000000000000000000000000000000000000000000000000000010000000000000000000000000000000000000000000000000000000000000000")
	report("shared-secret-err-007", privateKeyHex: "7e4aa54f714bf01df85c50269bea3a86721f84afe74f7b41ea58abcf3474e88d", peerPublicKeyHex: "0400000000000000000000000000000000000000000000000000000000000000010000000000000000000000000000000000000000000000000000000000000001")
	report("shared-secret-err-008", privateKeyHex: "7e4aa54f714bf01df85c50269bea3a86721f84afe74f7b41ea58abcf3474e88d", peerPublicKeyHex: "040000000000000000000000000000000000000000000000000000000000000001ffffffff00000001000000000000000000000000fffffffffffffffffffffffe")
	report("shared-secret-err-009", privateKeyHex: "7e4aa54f714bf01df85c50269bea3a86721f84afe74f7b41ea58abcf3474e88d", peerPublicKeyHex: "040000000000000000000000000000000000000000000000000000000000000001ffffffff00000001000000000000000000000000ffffffffffffffffffffffff")
	report("shared-secret-err-010", privateKeyHex: "7e4aa54f714bf01df85c50269bea3a86721f84afe74f7b41ea58abcf3474e88d", peerPublicKeyHex: "")
	report("shared-secret-err-011", privateKeyHex: "6f953faff3599e6c762d7f4cabfeed092de2add1df1bc5748c6cbb725cf35458", peerPublicKeyHex: "02fd4bf61763b46581fd9174d623516cf3c81edd40e29ffa2777fb6cb0ae3ce535")
	report("shared-secret-err-012", privateKeyHex: "d27edf0ff5b6b6b465753e7158370332c153b468a1be087ad0f490bdb99e5f02", peerPublicKeyHex: "03efdde3b32872a9effcf3b94cbf73aa7b39f9683ece9121b9852167f4e3da609b")
	report("shared-secret-err-013", privateKeyHex: "d27edf0ff5b6b6b465753e7158370332c153b468a1be087ad0f490bdb99e5f03", peerPublicKeyHex: "02efdde3b32872a9effcf3b94cbf73aa7b39f9683ece9121b9852167f4e3da609b")
	report("shared-secret-err-014", privateKeyHex: "95ead84540c2d027aa3130ff1b47888cc1ed67e8dda46156e71ce0991791e835", peerPublicKeyHex: "02c49524b2adfd8f5f972ef554652836e2efb2d306c6d3b0689234cec93ae73db5")
	report("shared-secret-err-015", privateKeyHex: "a8681ef67fb1f189647d95e8db00c52ceef6d41a85ba0a5bd74c44e8e62c8aa4", peerPublicKeyHex: "0318f9bae7747cd844e98525b7ccd0daf6e1d20a818b2175a9a91e4eae5343bc98")
	report("shared-secret-err-016", privateKeyHex: "a8681ef67fb1f189647d95e8db00c52ceef6d41a85ba0a5bd74c44e8e62c8aa5", peerPublicKeyHex: "0218f9bae7747cd844e98525b7ccd0daf6e1d20a818b2175a9a91e4eae5343bc98")
	report("shared-secret-err-017", privateKeyHex: "95ead84540c2d027aa3130ff1b47888cc1ed67e8dda46156e71ce0991791e834", peerPublicKeyHex: "03c49524b2adfd8f5f972ef554652836e2efb2d306c6d3b0689234cec93ae73db5")
	report("shared-secret-err-018", privateKeyHex: "0000000000000000000000000000000000000000000000000000000000000000", peerPublicKeyHex: "04700c48f77f56584c5cc632ca65640db91b6bacce3a4df6b42ce7cc838833d287db71e509e3fd9b060ddb20ba5c51dcc5948d46fbf640dfe0441782cab85fa4ac")
	report("shared-secret-err-019", privateKeyHex: "ffffffff00000000ffffffffffffffffbce6faada7179e84f3b9cac2fc632551", peerPublicKeyHex: "04700c48f77f56584c5cc632ca65640db91b6bacce3a4df6b42ce7cc838833d287db71e509e3fd9b060ddb20ba5c51dcc5948d46fbf640dfe0441782cab85fa4ac")
	report("shared-secret-err-020", privateKeyHex: "ffffffff00000000ffffffffffffffffbce6faada7179e84f3b9cac2fc632552", peerPublicKeyHex: "04700c48f77f56584c5cc632ca65640db91b6bacce3a4df6b42ce7cc838833d287db71e509e3fd9b060ddb20ba5c51dcc5948d46fbf640dfe0441782cab85fa4ac")
	report("shared-secret-err-021", privateKeyHex: "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff", peerPublicKeyHex: "04700c48f77f56584c5cc632ca65640db91b6bacce3a4df6b42ce7cc838833d287db71e509e3fd9b060ddb20ba5c51dcc5948d46fbf640dfe0441782cab85fa4ac")
	report("shared-secret-err-022", privateKeyHex: "00000000000000000000000000000000000000000000000000000000000001", peerPublicKeyHex: "04700c48f77f56584c5cc632ca65640db91b6bacce3a4df6b42ce7cc838833d287db71e509e3fd9b060ddb20ba5c51dcc5948d46fbf640dfe0441782cab85fa4ac")
	report("shared-secret-err-023", privateKeyHex: "000000000000000000000000000000000000000000000000000000000000000001", peerPublicKeyHex: "04700c48f77f56584c5cc632ca65640db91b6bacce3a4df6b42ce7cc838833d287db71e509e3fd9b060ddb20ba5c51dcc5948d46fbf640dfe0441782cab85fa4ac")
	report("shared-secret-err-024", privateKeyHex: "", peerPublicKeyHex: "04700c48f77f56584c5cc632ca65640db91b6bacce3a4df6b42ce7cc838833d287db71e509e3fd9b060ddb20ba5c51dcc5948d46fbf640dfe0441782cab85fa4ac")
	report("shared-secret-err-025", privateKeyHex: "7d7dc5f71eb29ddaf80d6214632eeae03d9058af1fb6d22ed80badb62bc1a534", peerPublicKeyHex: "00")
	report("shared-secret-err-026", privateKeyHex: "7d7dc5f71eb29ddaf80d6214632eeae03d9058af1fb6d22ed80badb62bc1a534", peerPublicKeyHex: "02700c48f77f56584c5cc632ca65640db91b6bacce3a4df6b42ce7cc838833d287")
	report("shared-secret-err-027", privateKeyHex: "7d7dc5f71eb29ddaf80d6214632eeae03d9058af1fb6d22ed80badb62bc1a534", peerPublicKeyHex: "06700c48f77f56584c5cc632ca65640db91b6bacce3a4df6b42ce7cc838833d287db71e509e3fd9b060ddb20ba5c51dcc5948d46fbf640dfe0441782cab85fa4ac")
	report("shared-secret-err-028", privateKeyHex: "7d7dc5f71eb29ddaf80d6214632eeae03d9058af1fb6d22ed80badb62bc1a534", peerPublicKeyHex: "04700c48f77f56584c5cc632ca65640db91b6bacce3a4df6b42ce7cc838833d287db71e509e3fd9b060ddb20ba5c51dcc5948d46fbf640dfe0441782cab85fa4")
	report("shared-secret-err-029", privateKeyHex: "7d7dc5f71eb29ddaf80d6214632eeae03d9058af1fb6d22ed80badb62bc1a534", peerPublicKeyHex: "04700c48f77f56584c5cc632ca65640db91b6bacce3a4df6b42ce7cc838833d287db71e509e3fd9b060ddb20ba5c51dcc5948d46fbf640dfe0441782cab85fa4ac00")
	report("shared-secret-err-030", privateKeyHex: "7d7dc5f71eb29ddaf80d6214632eeae03d9058af1fb6d22ed80badb62bc1a534", peerPublicKeyHex: "700c48f77f56584c5cc632ca65640db91b6bacce3a4df6b42ce7cc838833d287db71e509e3fd9b060ddb20ba5c51dcc5948d46fbf640dfe0441782cab85fa4ac")
	report("shared-secret-err-031", privateKeyHex: "7d7dc5f71eb29ddaf80d6214632eeae03d9058af1fb6d22ed80badb62bc1a534", peerPublicKeyHex: "")
	report("shared-secret-err-032", privateKeyHex: "7d7dc5f71eb29ddaf80d6214632eeae03d9058af1fb6d22ed80badb62bc1a534", peerPublicKeyHex: "0400000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000")
	report("shared-secret-err-033", privateKeyHex: "7d7dc5f71eb29ddaf80d6214632eeae03d9058af1fb6d22ed80badb62bc1a534", peerPublicKeyHex: "04700c48f77f56584c5cc632ca65640db91b6bacce3a4df6b42ce7cc838833d287db71e509e3fd9b060ddb20ba5c51dcc5948d46fbf640dfe0441782cab85fa4ad")
	report("shared-secret-err-034", privateKeyHex: "7d7dc5f71eb29ddaf80d6214632eeae03d9058af1fb6d22ed80badb62bc1a534", peerPublicKeyHex: "04ffffffff0000000100000000111124f400000000ffffffffffffffffffffffff0000000d12d381b0760b1c50be8acf859385052c7f53cde67ce13759de3123a0")
	report("shared-secret-err-035", privateKeyHex: "7d7dc5f71eb29ddaf80d6214632eeae03d9058af1fb6d22ed80badb62bc1a534", peerPublicKeyHex: "04000000000000000000000001ea77d449ffffffffffffffffffffffffffffffffffffffff7afbc0b425e820646dec622fb558a51d342aa257f4b6a8ec5ddf144e")
	report("shared-secret-err-036", privateKeyHex: "7d7dc5f71eb29ddaf80d6214632eeae03d9058af1fb6d22ed80badb62bc1a534", peerPublicKeyHex: "04ffffffff00000001000000000000000000000000ffffffffffffffffffffffff0000000000000000000000000000000000000000000000000000000000000000")
	report("shared-secret-err-037", privateKeyHex: "0000000000000000000000000000000000000000000000000000000000000000", peerPublicKeyHex: "00")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
shared-secret-220 d96412e31cf4d26195920cac952fb79ea25f6c50abc79b5ed0ef8026a6e83319
shared-secret-221 2b0eed9badc92a1068196dfec124fe8f9d3f451e294d322eb881cce02f286026
shared-secret-222 ed0b1d8dfd27a61fce91dc6405bfc53b6d48a8c13ba541c96ef3dcf31d7cdb88
shared-secret-223 7e83fd2c3d713bc85d6d85d9078b3a0842824d410e8abde04da0fd71c7d94705
shared-secret-224 0e3dfdab606ebdc6428282acd443f189c99b3b483aa101fd8d6bed38aec59e02
shared-secret-225 f489f2bd93f76b8e41fc6b9f211bc599d49db1f17a38e95bab1d31b2a2b55829
shared-secret-226 cc5738b49d30d5d02cf7e0c54a3de09b5b6f3c4dea91dd0679072a3562444c37
shared-secret-227 89a11177d6907a81d47467093bf6a3cc8ba55dee05239b160a31a3000f5d807b
shared-secret-228 2cb03c30b20037a5cf4d5b33574f3abac895bfab37867eb2ebed260e0929058d
shared-secret-229 e54d487d0c4b12fe522af3e663ce316e632ba9d63a1f02a36fc5a82bf82731a4
shared-secret-230 ccea969d40fa42933f4fbdc4cabe2185f8a452996254c1f4e0dde5e14feeea8d
shared-secret-231 4ce2701b2be63a0083a4c53f7a0bf04cf871654f5edb6f625e3ea5e7d0bdcc90
shared-secret-232 7abef9765cca721320fbf8edcbef6d2ba25d17b70ffa1776029bc38fe677a12c
shared-secret-233 5f61404dbbbc2867dff95c1f37ed44f4cb8fabcd223b03739d888308d13bc412
shared-secret-234 8d673a577e35bf9d5d00676c08b2c739617c46a052188403aa06dc714af6acc1
shared-secret-235 a7835ffee0f2a69dfcf70d4e798dbe3ed32ba03cfddae5ddd11d8c0ac3d74f9b
shared-secret-236 e98ea22209cd397edb6c319648c1eb24bc4d39598ab11995571926684ce2ceca
shared-secret-237 91b65733860b1bdb9541d9f55895a3dbb3f13c199251d33006b6dcf90ac349ed
shared-secret-238 1fdf7c5c48047a113e5e5d1b7ed593337e769231cca5c7110160e0c1b97f4256
shared-secret-239 85a0b58519b28e70a694ec5198f72c4bfdabaa30a70f7143b5b1cd7536f716ca
shared-secret-240 a329a7d80424ea2d6c904393808e510dfbb28155092f1bac284dceda1f13afe5
shared-secret-241 bd26d0293e8851c51ebe0d426345683ae94026aca545282a4759faa85fde6687
shared-secret-242 ea9350b2490a2010c7abf43fb1a38be729a2de375ea7a6ac34ff58cc87e51b6c
shared-secret-243 34eed3f6673d340b6f716913f6dfa36b5ac85fa667791e2d6a217b0c0b7ba807
shared-secret-244 1354ce6692c9df7b6fc3119d47c56338afbedccb62faa546c0fe6ed4959e41c3
shared-secret-245 fe7496c30d534995f0bf428b5471c21585aaafc81733916f0165597a55d12cb4
shared-secret-246 348bf8042e4edf1d03c8b36ab815156e77c201b764ed4562cfe2ee90638ffef5
shared-secret-247 4d4de80f1534850d261075997e3049321a0864082d24a917863366c0724f5ae3
shared-secret-248 831c3f6b5f762d2f461901577af41354ac5f228c2591f84f8a6e51e2e3f17991
shared-secret-249 700c48f77f56584c5cc632ca65640db91b6bacce3a4df6b42ce7cc838833d287
shared-secret-250 e697267b1ae1fe830ff9ac568180b9e81c324b4845938feb29234a609dbe04f6
shared-secret-251 700c48f77f56584c5cc632ca65640db91b6bacce3a4df6b42ce7cc838833d287
shared-secret-252 e697267b1ae1fe830ff9ac568180b9e81c324b4845938feb29234a609dbe04f6
shared-secret-253 ead218590119e8876b29146ff89ca61770c4edbbf97d38ce385ed281d8a6b230
shared-secret-err-001 invalidPublicKey
shared-secret-err-002 invalidPublicKey
shared-secret-err-003 invalidPublicKey
shared-secret-err-004 invalidPublicKey
shared-secret-err-005 invalidPublicKey
shared-secret-err-006 invalidPublicKey
shared-secret-err-007 invalidPublicKey
shared-secret-err-008 invalidPublicKey
shared-secret-err-009 invalidPublicKey
shared-secret-err-010 invalidPublicKey
shared-secret-err-011 invalidPublicKey
shared-secret-err-012 invalidPublicKey
shared-secret-err-013 invalidPublicKey
shared-secret-err-014 invalidPublicKey
shared-secret-err-015 invalidPublicKey
shared-secret-err-016 invalidPublicKey
shared-secret-err-017 invalidPublicKey
shared-secret-err-018 invalidPrivateKey
shared-secret-err-019 invalidPrivateKey
shared-secret-err-020 invalidPrivateKey
shared-secret-err-021 invalidPrivateKey
shared-secret-err-022 invalidPrivateKey
shared-secret-err-023 invalidPrivateKey
shared-secret-err-024 invalidPrivateKey
shared-secret-err-025 invalidPublicKey
shared-secret-err-026 invalidPublicKey
shared-secret-err-027 invalidPublicKey
shared-secret-err-028 invalidPublicKey
shared-secret-err-029 invalidPublicKey
shared-secret-err-030 invalidPublicKey
shared-secret-err-031 invalidPublicKey
shared-secret-err-032 invalidPublicKey
shared-secret-err-033 invalidPublicKey
shared-secret-err-034 invalidPublicKey
shared-secret-err-035 invalidPublicKey
shared-secret-err-036 invalidPublicKey
shared-secret-err-037 invalidPrivateKey
```
