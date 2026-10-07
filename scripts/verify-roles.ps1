# Verifies role-based auth against a running API (demo users must be seeded).
# Usage: ./verify-roles.ps1 [-Base http://localhost:5080] [-ExpiredBase http://localhost:5081]
# -ExpiredBase points at a second API instance started with Jwt__AccessMinutes=-2 (tokens already expired).
param([string]$Base = 'http://localhost:5080', [string]$ExpiredBase = '')
$script:fail = 0
function Check($name, $ok) { if ($ok) { "PASS  $name" } else { "FAIL  $name"; $script:fail++ } }
function Call($method, $url, $token, $body) {
  $h = @{}; if ($token) { $h.Authorization = "Bearer $token" }
  try {
    $a = @{ Uri = $url; Method = $method; Headers = $h; UseBasicParsing = $true }
    if ($body) { $a.Body = ($body | ConvertTo-Json); $a.ContentType = 'application/json' }
    $r = Invoke-WebRequest @a
    return @{ Status = [int]$r.StatusCode; Json = ($r.Content | ConvertFrom-Json -ErrorAction SilentlyContinue) }
  } catch { return @{ Status = [int]$_.Exception.Response.StatusCode; Json = $null } }
}
function JwtPayload($jwt) {
  $p = $jwt.Split('.')[1].Replace('-', '+').Replace('_', '/'); while ($p.Length % 4) { $p += '=' }
  [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($p)) | ConvertFrom-Json
}
$demo = @(
  @{ u = 'admin'; p = 'Admin@123'; role = 'ADMIN' }, @{ u = 'agent'; p = 'Agent@123'; role = 'AGENT' },
  @{ u = 'underwriter'; p = 'Underwriter@123'; role = 'UNDERWRITER' }, @{ u = 'manager'; p = 'Manager@123'; role = 'MANAGER' },
  @{ u = 'customer'; p = 'Customer@123'; role = 'CUSTOMER' })
$matrix = @(
  @{ path = '/api/customers';          exp = @{ ADMIN = 200; AGENT = 200; UNDERWRITER = 403; MANAGER = 200; CUSTOMER = 403 } },
  @{ path = '/api/users';              exp = @{ ADMIN = 200; AGENT = 403; UNDERWRITER = 403; MANAGER = 403; CUSTOMER = 403 } },
  @{ path = '/api/roles';              exp = @{ ADMIN = 200; AGENT = 403; UNDERWRITER = 403; MANAGER = 403; CUSTOMER = 403 } },
  @{ path = '/api/reports/summary';    exp = @{ ADMIN = 200; AGENT = 403; UNDERWRITER = 200; MANAGER = 200; CUSTOMER = 403 } },
  @{ path = '/api/underwriting/queue'; exp = @{ ADMIN = 403; AGENT = 403; UNDERWRITER = 200; MANAGER = 403; CUSTOMER = 403 } },
  @{ path = '/api/quotes';             exp = @{ ADMIN = 200; AGENT = 200; UNDERWRITER = 200; MANAGER = 200; CUSTOMER = 200 } })
$travel = @{ destinationCode = 'FR'; startDate = '2027-01-01'; endDate = '2027-01-05'; travellers = 1; tripCost = 1000 }
$motor = @{ make = 'A'; model = 'B'; year = 2022; vehicleValue = 10000; driverAge = 40; claimsLast5Years = 0; coverageCode = 'COMPREHENSIVE' }
$motorExp = @{ ADMIN = 201; AGENT = 201; UNDERWRITER = 403; MANAGER = 403; CUSTOMER = 403 }
$travelExp = @{ ADMIN = 201; AGENT = 201; UNDERWRITER = 201; MANAGER = 201; CUSTOMER = 403 }

foreach ($d in $demo) {
  $r = Call POST "$Base/api/v1/auth/login" $null @{ username = $d.u; password = $d.p }
  Check "$($d.role) login -> 200 + success envelope" ($r.Status -eq 200 -and $r.Json.success -eq $true -and $r.Json.message -eq 'Login successful')
  $data = $r.Json.data; $tok = $data.accessToken
  Check "$($d.role) response user.role" ($data.user.role -eq $d.role)
  Check "$($d.role) JWT role claim" ((JwtPayload $tok).role -eq $d.role)
  Check "$($d.role) permissions returned and in JWT" ($data.user.permissions.Count -gt 0 -and @((JwtPayload $tok).permission).Count -eq $data.user.permissions.Count)
  Check "$($d.role) expiresIn > 0" ($data.expiresIn -gt 0)
  foreach ($m in $matrix) {
    $s = (Call GET "$Base$($m.path)" $tok).Status
    Check "$($d.role) GET $($m.path) -> $($m.exp[$d.role])" ($s -eq $m.exp[$d.role])
  }
  $s = (Call POST "$Base/api/quotes/motor" $tok $motor).Status;   Check "$($d.role) POST motor -> $($motorExp[$d.role])" ($s -eq $motorExp[$d.role])
  $s = (Call POST "$Base/api/quotes/travel" $tok $travel).Status; Check "$($d.role) POST travel -> $($travelExp[$d.role])" ($s -eq $travelExp[$d.role])
  Check "$($d.role) logout" ((Call POST "$Base/api/v1/auth/logout" $null @{ refreshToken = $data.refreshToken }).Status -eq 200)
  Check "$($d.role) refresh token revoked after logout" ((Call POST "$Base/api/v1/auth/refresh" $null @{ refreshToken = $data.refreshToken }).Status -eq 401)
}
Check 'wrong password -> 401' ((Call POST "$Base/api/v1/auth/login" $null @{ username = 'admin'; password = 'nope' }).Status -eq 401)
Check 'invalid username -> 401' ((Call POST "$Base/api/v1/auth/login" $null @{ username = 'ghost'; password = 'x' }).Status -eq 401)
Check 'no token GET /api/quotes -> 401' ((Call GET "$Base/api/quotes" $null).Status -eq 401)
Check 'no token GET /api/users -> 401' ((Call GET "$Base/api/users" $null).Status -eq 401)
Check 'no token POST motor -> 401' ((Call POST "$Base/api/quotes/motor" $null $motor).Status -eq 401)
Check 'tampered token -> 401' ((Call GET "$Base/api/users" 'eyJhbGciOiJIUzI1NiJ9.eyJyb2xlIjoiQURNSU4ifQ.bad').Status -eq 401)
if ($ExpiredBase) {
  $r = Call POST "$ExpiredBase/api/v1/auth/login" $null @{ username = 'admin'; password = 'Admin@123' }
  Check 'expired access token -> 401' ((Call GET "$ExpiredBase/api/quotes" $r.Json.data.accessToken).Status -eq 401)
}
"`n$script:fail failure(s)"
exit $script:fail

