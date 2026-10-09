# Hướng dẫn sử dụng Test Case Management

Tài liệu thực hành cho cả đội: tester, QA lead và product owner. Đọc phần khái niệm mất khoảng 10 phút; làm hết 10 bài tập mất khoảng 60 phút.

**Mọi bài tập trong tài liệu này đã được làm thật, từng bước, trên giao diện, bằng đúng tài khoản của vai trò đó**, và các ảnh chụp lấy từ chính lần làm đó (ô vàng là chỗ cần chú ý).

- [1. Ý tưởng chính](#1-ý-tưởng-chính)
- [2. Ba sơ đồ cần nhớ](#2-ba-sơ-đồ-cần-nhớ)
- [3. Trước khi bắt đầu](#3-trước-khi-bắt-đầu)
- [4. Thực hành](#4-thực-hành)
  - [Phần A: Tester (Bài 1 đến 4)](#phần-a-tester)
  - [Phần B: QA Lead (Bài 5 đến 10)](#phần-b-qa-lead)
- [5. Quy ước của đội](#5-quy-ước-của-đội)
- [6. Hỏi đáp và xử lý sự cố](#6-hỏi-đáp-và-xử-lý-sự-cố)
- [Phụ lục: quyền theo vai trò](#phụ-lục-quyền-theo-vai-trò)

> Các bài tập dùng **bộ dữ liệu mẫu EasyInvoice** (hóa đơn điện tử) trên môi trường tập. Tên nút theo giao diện tiếng Anh (**Execute**, **Approve**...). Số liệu và thời gian trong ảnh có thể khác với của bạn đôi chút.

---

## 1. Ý tưởng chính

| Khái niệm | Hiểu đơn giản |
|---|---|
| **Dự án** (project) | Lớp trên cùng. Mỗi dự án (ví dụ EINV, HRM) có bộ test, test case, plan, yêu cầu và run riêng, không lẫn vào nhau. Chọn dự án đang làm ở đầu mỗi trang. |
| **Yêu cầu** (requirement) | Điều sản phẩm phải làm được, ví dụ "Ký số hóa đơn bằng chứng thư số hợp lệ". |
| **Bộ test** (suite) | Thư mục chứa test case, lồng được nhiều cấp, chia theo luồng nghiệp vụ. |
| **Test case** | Một kịch bản kiểm thử: các bước, kết quả mong đợi, dữ liệu thử. |
| **Phiên bản** (version) | Mỗi lần test case được **duyệt** thì có một phiên bản. Sửa test case đã duyệt thì nó **quay về Under review** và chỉ có phiên bản mới khi được duyệt lại; các lần chạy cũ vẫn giữ nguyên bản đã chạy. |
| **Bước dùng chung** | Đoạn bước dùng lại ở nhiều test case (ví dụ "Đăng nhập"). Sửa một chỗ, cập nhật được mọi nơi dùng. |
| **Plan** | Một đợt kiểm thử, thường tương ứng một đợt phát hành. |
| **Run** | Một lần chạy một nhóm test case trên một môi trường (Staging, UAT). Mỗi test case trong run là một **mục chạy**, được giao cho một người. |
| **Lần thử** (attempt) | Mỗi lần ghi kết quả cho một mục chạy. Chạy lại thì thêm lần thử mới, **không ghi đè** lần cũ. |
| **Cổng chất lượng** | Bộ điều kiện để được phát hành (tỷ lệ đạt tối thiểu, không còn lỗi Critical mở...). |
| **Sign-off** | Chữ ký xác nhận phát hành của đủ số người duyệt, sau khi cổng chất lượng đạt. |

## 2. Ba sơ đồ cần nhớ

### 2.1 Luồng tổng thể

```mermaid
flowchart LR
    REQ[Yêu cầu] --> TC[Test case<br/>viết và duyệt]
    TC --> RUN[Plan và run<br/>giao cho người test]
    RUN --> RES[Kết quả<br/>lỗi và bằng chứng]
    RES --> TRACE[Truy vết yêu cầu<br/>và Dashboard]
    RES --> GATE[Cổng chất lượng]
    GATE --> SIGN[Sign-off<br/>cho phép phát hành]
```

### 2.2 Vòng đời của một test case

```mermaid
stateDiagram-v2
    [*] --> Draft: tạo mới
    Draft --> UnderReview: Submit for review
    Draft --> Approved: Approve (duyệt thẳng)
    UnderReview --> Approved: Approve (QA Lead)
    UnderReview --> Draft: Back to draft (trả lại để sửa)
    Approved --> UnderReview: Edit (sửa nội dung)
    Approved --> Draft: Back to draft
    Approved --> Deprecated: Deprecate (ngừng dùng)
    Deprecated --> Draft: Back to draft
```

Chỉ test case **Approved** mới đưa được vào run. Sửa test case đã duyệt không làm hỏng các run cũ vì chúng giữ đúng phiên bản đã chạy.

### 2.3 Ai làm gì

```mermaid
flowchart TB
    subgraph Tester
      T1[Viết test case nháp]
      T2[Chạy test và ghi kết quả]
      T3[Gắn lỗi, đính kèm log và ảnh]
    end
    subgraph QA Lead
      Q1[Duyệt test case]
      Q2[Lập plan và run, giao việc]
      Q3[Cấu hình cổng chất lượng]
      Q4[Bắt đầu sign-off]
    end
    subgraph Product Owner
      P1[Quản lý yêu cầu]
      P2[Xem dashboard và truy vết]
      P3[Duyệt sign-off]
    end
```

## 3. Trước khi bắt đầu

1. Mở địa chỉ của môi trường tập (do QA Lead cung cấp), bấm **Login** và đăng nhập.
2. Trong thanh bên trái, mở nhóm **Test Case Management**. Chỉ những mục bạn có quyền mới hiện ra.
3. Nếu không thấy nhóm này, bạn chưa được cấp quyền: nhờ quản trị viên thêm quyền ở **Administration → Roles** (xem [phụ lục](#phụ-lục-quyền-theo-vai-trò)).

Các tài khoản dùng trong tài liệu (mật khẩu môi trường tập do QA Lead cấp):

| Tài khoản | Vai trò | Dùng ở |
|---|---|---|
| `lan.nguyen` | Tester | Bài 1 đến 4 |
| `hoanganh.le` | QA Lead | Bài 5 đến 10 |
| `thuy.pham` | Product Owner | Bài 9 (người duyệt thứ hai) |

Ở đầu mỗi trang có ô **Project** để chọn dự án đang làm (xem [Bài 10](#bài-10-làm-việc-với-nhiều-dự-án)). Các ảnh chụp ở Bài 1 đến 9 được chụp trước khi có ô này, nên chưa thấy nó; mọi thao tác vẫn như mô tả.

Các mục trong thanh bên: **Dashboard**, **Test repository**, **Shared steps**, **Plans and runs**, **Traceability**, **Quality and sign-off**. Mỗi vai trò chỉ thấy những mục mình có quyền.

![Thanh bên của Tester](images/guide/b1-01-thanh-ben.png)

## 4. Thực hành

### Phần A: Tester

*Tình huống:* QA Lead đã giao cho bạn (Nguyễn Thị Lan) một số test trong đợt hồi quy build 2.5.0-rc2. Bạn chạy chúng và ghi kết quả.

#### Bài 1: Tìm việc của mình và ghi kết quả Passed

1. Vào **Plans and runs**. Danh sách có các run và plan. Mở run **"Hồi quy đầy đủ build 2.5.0-rc2"**.

   ![Danh sách run](images/guide/b1-02-danh-sach-run.png)

2. Trang run hiện các ô số (tổng, đạt, lỗi...) và bảng các mục chạy. Phía trên bảng có ô **Show**, mặc định là *All testers*.

   ![Mở run](images/guide/b1-03-mo-run.png)

3. Chọn **My tests** để chỉ thấy việc được giao cho bạn. Bảng thu gọn lại; dòng nào còn **Untested** là việc bạn phải làm, và nút **Execute** nằm ở cuối dòng.

   ![Việc của tôi](images/guide/b1-04-viec-cua-toi.png)

   > Bạn sẽ thấy cột **Tester** ghi "Assigned" thay cho tên người. Đó là bình thường với tài khoản Tester (ứng dụng không cho Tester xem danh sách người dùng); bộ lọc "My tests" vẫn đúng.

4. Bấm **Execute** ở dòng `EINV-API-002`. Một ngăn kéo hiện ra từ bên phải. Làm theo các bước của test case, chọn **Result = Passed**, ghi thời gian (giây) nếu muốn, rồi bấm **Record result**.

   ![Ghi Passed](images/guide/b1-05-ghi-passed.png)

5. Ngăn kéo đóng lại, có thông báo xác nhận, dòng đó chuyển thành **Passed** và số **Attempts** là 1.

   ![Đã ghi](images/guide/b1-06-da-ghi.png)

> Mẹo: ngăn kéo kéo đổi được độ rộng. Kéo thanh nhỏ ở mép trái của nó, hoặc dùng mũi tên trái/phải khi đang chọn thanh đó. Độ rộng được nhớ cho lần sau.

#### Bài 2: Ghi kết quả Failed kèm lỗi và bằng chứng

1. Bấm **Execute** ở dòng `EINV-RPT-002`. Chọn **Result = Failed**.
2. Ở **Actual result**, mô tả **điều gì đã xảy ra thật** (không chỉ viết "lỗi"). Ví dụ: *"Tồn cuối kỳ lệch 2 hóa đơn so với bảng tổng hợp tay: hệ thống 148, tay 150"*.
3. Trong **Defects found**, bấm **+ Link a defect**. Ô đầu là hệ thống quản lý lỗi (mặc định Jira), ô thứ hai là **khóa lỗi** (ví dụ `EINV-1300`). Bạn tạo lỗi trên Jira trước rồi dán khóa vào đây.
4. Bấm **Record result**.

   ![Ghi Failed](images/guide/b2-01-ghi-failed.png)

5. Dòng chuyển thành **Failed**, và số lỗi mở của run tăng lên.

   ![Đã ghi Failed](images/guide/b2-02-da-ghi-failed.png)

6. Đính kèm bằng chứng: ở dòng đó bấm **History**. Mỗi lần thử có một vùng đính kèm nét đứt. Kéo thả tệp vào đó, hoặc bấm vào vùng đó để chọn tệp, hoặc bấm vào vùng đó rồi **dán ảnh chụp màn hình** bằng Ctrl+V.

   ![Lịch sử lần thử](images/guide/b2-03-lich-su-lan-thu.png)

7. Tệp được tải lên và hiện ngay dưới lần thử, cùng khóa lỗi đã gắn (`Jira EINV-1300`, mức độ, trạng thái Open).

   ![Đã đính kèm](images/guide/b2-04-da-dinh-kem.png)

8. Bấm vào tên tệp: ảnh và log **mở xem ngay trong ứng dụng** (các loại tệp khác sẽ được tải về). Nút **Download** để tải về nếu cần.

   ![Xem log](images/guide/b2-05-xem-log.png)

#### Bài 3: Chạy lại sau khi lỗi được sửa (retest)

1. Giả sử dev đã sửa lỗi `EINV-1300`. Ở dòng test đã Failed, bấm **Retest**.

   ![Nút Retest](images/guide/b3-01-nut-retest.png)

2. Mô tả điều bạn thấy lần này, chọn **Result = Passed** và bấm **Record result**.

   ![Ghi Passed lần 2](images/guide/b3-02-ghi-passed-lan-2.png)

3. Bấm **History**: bạn thấy **hai lần thử** (Failed rồi Passed). Lần cũ, cùng lỗi và tệp log của nó, **không bị xóa**. Trạng thái hiện tại của test là lần thử mới nhất.

   ![Hai lần thử](images/guide/b3-03-hai-lan-thu.png)

> Nếu một test cứ lúc Pass lúc Fail dù code không đổi, hệ thống sẽ đánh dấu nó là **flaky** (chập chờn) trên Dashboard (xem Bài 8). Hãy ghi lại điều đó trong kết quả thực tế để cả đội xử lý.

#### Bài 4: Viết test case mới, dùng AI gợi ý bước

1. Vào **Test repository**, chọn bộ test cần thêm ở cây bên trái (ví dụ **Hóa đơn GTGT**), bấm **+ New test case**.

   ![Chọn bộ test](images/guide/b4-01-chon-bo-test.png)

2. Điền **Code** (theo [quy ước](#5-quy-ước-của-đội)), **Title**, độ ưu tiên và mức nghiêm trọng. Ô **Suite** đã tự chọn theo bộ test bạn vừa chọn.

   ![Điền thông tin](images/guide/b4-02-dien-thong-tin.png)

3. Cuộn xuống phần **Steps**, bấm **Suggest steps with AI**. Dán yêu cầu hoặc tiêu chí chấp nhận vào ô **Requirement** rồi bấm **Generate steps** (mất vài giây, tối đa 4000 ký tự).

   ![Nhập yêu cầu](images/guide/b4-03-nhap-yeu-cau.png)

4. AI đề xuất các bước. **Tích chọn** bước muốn giữ rồi bấm **Add … step(s) to the test case**.

   ![Đề xuất của AI](images/guide/b4-04-de-xuat-cua-ai.png)

   > AI trả lời theo **ngôn ngữ giao diện đang chọn**: giao diện tiếng Anh thì các bước bằng tiếng Anh, giao diện tiếng Việt thì tiếng Việt. Đoạn yêu cầu bạn nhập được gửi tới dịch vụ AI do quản trị viên cấu hình, nên đừng dán dữ liệu mật.

5. Các bước được thêm vào form, **chưa lưu**. **Đọc kỹ và sửa** cho đúng với hệ thống thật (tên nút, tên màn hình): AI chỉ gợi ý, người viết chịu trách nhiệm nội dung. Sau đó bấm **Save**.

   ![Các bước đã thêm](images/guide/b4-05-buoc-da-them.png)

6. Test case xuất hiện trong danh sách ở trạng thái **Draft**, phiên bản v0. Nhờ QA Lead duyệt (Bài 5).

   ![Trạng thái Draft](images/guide/b4-06-trang-thai-draft.png)

### Phần B: QA Lead

*Tình huống:* bạn là Lê Hoàng Anh, QA Lead. Bạn duyệt test case của đồng nghiệp, bảo trì bước dùng chung, lập đợt chạy mới, theo dõi chất lượng và cho phép phát hành.

#### Bài 5: Duyệt test case

1. Vào **Test repository**, đặt bộ lọc **Any status → Draft** để thấy các test case đang chờ. Test case của Lan, `EINV-INV-007`, nằm trong đó.

   ![Lọc Draft](images/guide/b5-01-loc-draft.png)

2. Bấm vào test case để mở ngăn kéo chi tiết và đọc kỹ từng bước. Nút ở cuối ngăn kéo: **Submit for review** (gửi xem xét), **Approve** (duyệt), **Delete**, **Edit**.

   ![Đọc các bước](images/guide/b5-02-doc-cac-buoc.png)

3. Nếu ổn, bấm **Approve**. (Test case Draft duyệt thẳng được; hoặc đi qua **Submit for review** nếu đội muốn có bước xem xét riêng. Nếu chưa ổn, báo người viết sửa.) Lọc lại theo **Approved** và tìm mã: test case đã ở trạng thái **Approved**, phiên bản v1.

   ![Đã duyệt](images/guide/b5-03-da-duyet.png)

#### Bài 6: Bảo trì bước dùng chung

1. Vào **Shared steps**: danh sách các nhóm bước, số bước, bản sửa (revision) và số test case đang dùng. Nhóm "Đăng nhập EasyInvoice bằng tài khoản kế toán" được dùng ở nhiều test case.

   ![Danh sách nhóm](images/guide/b6-01-danh-sach-nhom.png)

2. Bấm **Usage** để xem những test case nào đang dùng nhóm này và chúng còn **Up to date** hay không.

   ![Nơi đang dùng](images/guide/b6-02-noi-dang-dung.png)

3. Đóng lại, bấm biểu tượng **bút chì (Edit)** ở dòng nhóm. Sửa nội dung một bước (ví dụ kết quả mong đợi của bước 2) rồi bấm **Save**. Nhóm lên bản sửa 2. **Chưa test case nào tự thay đổi** cho đến khi bạn cập nhật.

   ![Sửa nhóm](images/guide/b6-03-sua-nhom.png)

4. Mở một test case có dùng nhóm (ví dụ `EINV-AUTH-003`). Ở mục **Shared steps** nó đã báo **Behind** (cũ hơn nhóm), kèm hai nút **Update** và **Detach**.

   ![Bị cũ hơn nhóm](images/guide/b6-04-bi-cu-hon-nhom.png)

5. Bấm **Update**. Vì test case đã **Approved**, hệ thống hỏi xác nhận vì thay đổi sẽ **đưa nó về xem xét lại**. Bấm **Continue**.

   ![Xác nhận phiên bản mới](images/guide/b6-05-xac-nhan-phien-ban-moi.png)

6. Test case cập nhật theo nhóm: nhãn chuyển thành **Up to date**, nhưng trạng thái là **Under review** và có dải thông báo vàng: các run vẫn dùng phiên bản đã duyệt (v1) cho đến khi test case được duyệt lại.

   ![Đã cập nhật, chờ duyệt lại](images/guide/b6-06-da-cap-nhat.png)

7. Người có quyền duyệt bấm **Approve**. Hộp thoại hỏi **đã thay đổi gì** (không bắt buộc, để trống cũng được; ghi lại thì lịch sử phiên bản dễ đọc hơn). Bấm **Approve**.

   ![Duyệt lại](images/guide/b6-07-duyet-lai.png)

8. Test case trở lại **Approved**, phiên bản tăng lên v2 và ghi chú nằm trong **Version history**. Người duyệt có thể chính là người sửa.

   ![Phiên bản mới](images/guide/b6-08-phien-ban-moi.png)

#### Bài 7: Tạo plan, run và giao việc

1. Vào **Plans and runs**, bấm **+ New plan**. Đặt tên theo đợt phát hành (ví dụ "EasyInvoice 2.6 - Phát hành tháng 12"), thêm mô tả, bấm **Save**.

   ![Tạo plan](images/guide/b7-01-tao-plan.png)

2. Plan mới ở trạng thái **Draft**. Bấm nút `⋯` ở dòng plan, chọn **Active** trong mục "Move to".

   ![Chuyển sang Active](images/guide/b7-02-chuyen-active.png)

3. Bấm **+ New run**: nhập **Title** và **Environment**, chọn **Plan**. Trong danh sách test case (chỉ test case **Approved** mới hiện), gõ vào ô tìm kiếm để lọc rồi tích chọn các test case cần chạy (dòng "N selected" cho biết đã chọn bao nhiêu). Bấm **Create run**.

   ![Chọn test case](images/guide/b7-03-chon-test-case.png)

4. Run mới ở trạng thái **Planned**, các mục đều **Untested** và chưa giao cho ai.

   ![Run mới](images/guide/b7-04-run-moi.png)

5. Ở cột **Tester**, chọn người chạy cho từng dòng; lưu ngay khi chọn. Ô **Show** phía trên cho biết khối lượng mỗi người.

   ![Giao việc](images/guide/b7-05-giao-viec.png)

6. Cần thêm test case: bấm **+ Add test cases**. Ô **Assign the new test cases to** cho phép chọn ngay người nhận cho các test case vừa thêm.

   ![Thêm test case, giao ngay](images/guide/b7-06-them-test-giao-ngay.png)

7. Test case mới xuất hiện cùng người được giao.

   ![Sau khi thêm](images/guide/b7-07-sau-khi-them.png)

> Khi mọi mục đã chạy xong, bấm **Complete run** để đóng run.

#### Bài 8: Đọc Dashboard và Traceability

1. Vào **Dashboard**. Các ô số trên cùng: *Pass rate* (tỷ lệ đạt), *Completion* (tỷ lệ đã chạy), tốc độ chạy mỗi ngày, mật độ lỗi trên 100 test đã chạy, số test chập chờn. Ô chọn ở góc trên phải mặc định là *All runs* (mọi run).

   ![Dashboard](images/guide/b8-01-dashboard.png)

2. Chọn một **plan** để xem riêng từng đợt: mọi số liệu và biểu đồ đổi theo plan đó.

   ![Dashboard theo plan](images/guide/b8-02-dashboard-theo-plan.png)

3. Cuộn xuống **Flaky tests**: test lúc đạt lúc fail (điểm từ 0,3 trở lên là *Flaky*, từ 0,15 là *Watch*). Cột *Passed / Failed / Flips* cho số lần đạt, fail và số lần đổi chiều. Bấm **Flag flaky tests in the library** để đánh dấu cờ vào test case.

   ![Test chập chờn](images/guide/b8-03-test-chap-chon.png)

4. Vào **Traceability**. Mỗi yêu cầu có một trạng thái: *Passed / Failed / Blocked / Not run / Uncovered*. Các ô số trên cùng cho biết yêu cầu nào **chưa có test nào** (**Uncovered**).

   ![Truy vết](images/guide/b8-04-truy-vet.png)

5. Cuộn xuống tìm yêu cầu *Uncovered* (ví dụ "Khởi tạo hóa đơn từ máy tính tiền"): cột test case ghi "No test case linked". Đây là chỗ cần viết thêm test trước khi phát hành.

   ![Yêu cầu chưa có test](images/guide/b8-05-yeu-cau-chua-co-test.png)

6. Để gắn test case có sẵn vào một yêu cầu, bấm **Link tests** ở dòng yêu cầu đó. Tìm test case, tích chọn rồi bấm **Link**.

   ![Gắn test case](images/guide/b8-06-gan-test-case.png)

7. Test case được gắn xuất hiện ngay trong cột "Linked test cases" của yêu cầu.

   ![Đã gắn](images/guide/b8-07-da-gan.png)

#### Bài 9: Cổng chất lượng và sign-off

1. Vào **Quality and sign-off**. Chọn plan ở ô đầu, chọn cổng (mặc định *Default gate*) rồi bấm **Evaluate**.

   ![Màn hình chất lượng](images/guide/b9-01-man-hinh-chat-luong.png)

2. Với plan **chưa sẵn sàng** (còn lỗi mở), kết quả là **Gate not passed**. Bảng liệt kê từng tiêu chí: giá trị yêu cầu, giá trị thực tế và Pass/Fail. Cổng còn xét **lỗi Critical/High đang mở** và việc **mọi test ưu tiên P1 đã chạy**, không chỉ tỷ lệ đạt. Nút **Start sign-off** bị khóa cho tới khi đạt.

   ![Cổng không đạt](images/guide/b9-02-cong-khong-dat.png)

3. Với plan **đã sẵn sàng** (mọi test đã chạy và đạt, không còn lỗi mở), kết quả là **Gate passed** và nút **Start sign-off** mở ra.

   ![Cổng đạt](images/guide/b9-03-cong-dat.png)

4. Bấm **Start sign-off**. Ghi **vai trò** của bạn (ví dụ QA Lead) và nhận xét, rồi bấm **Sign**. Hệ thống đánh giá lại cổng và đóng băng số liệu, kèm mã băm SHA-256.

   ![Bắt đầu sign-off](images/guide/b9-04-bat-dau-sign-off.png)

5. Báo cáo sign-off xuất hiện ở trạng thái **Pending**, đã có **1 / 2** chữ ký. Cần thêm người duyệt khác.

   ![Chờ người thứ hai](images/guide/b9-05-cho-nguoi-thu-hai.png)

6. **Người duyệt thứ hai** (ở đây là product owner Phạm Thanh Thủy) đăng nhập, vào cùng màn hình và bấm **Approve** ở dòng báo cáo.

   ![Người thứ hai duyệt](images/guide/b9-06-nguoi-thu-hai-duyet.png)

7. Ghi vai trò (Product Owner) và nhận xét, bấm **Sign**.

   ![Người thứ hai ký](images/guide/b9-07-nguoi-thu-hai-ky.png)

8. Đủ **2 / 2** chữ ký: báo cáo chuyển thành **Approved**, và cột *Integrity* là **Verified** (số liệu không bị sửa sau khi ký).

   ![Đã sign-off](images/guide/b9-08-da-sign-off.png)

9. Bấm vào tên báo cáo để xem chi tiết: bảng tiêu chí đã đóng băng, mã băm SHA-256 và danh sách người ký (ai, vai trò, khi nào, nhận xét).

   ![Chi tiết báo cáo](images/guide/b9-09-chi-tiet-bao-cao.png)

#### Bài 10: Làm việc với nhiều dự án

Khi công ty có nhiều sản phẩm (ví dụ EasyInvoice và HRM), mỗi sản phẩm là một **dự án**. Mỗi dự án có bộ test, test case, plan, yêu cầu và run riêng. Phần này cho thấy cách chọn, tạo và lưu trữ dự án.

1. Ở **đầu mọi trang** có ô **Project**. Đó là dự án bạn đang làm: mọi danh sách, dashboard và truy vết chỉ hiện dữ liệu của dự án này, và những gì bạn tạo ra cũng nằm trong dự án này. Dữ liệu có từ trước khi có dự án nằm trong dự án **DEFAULT** (bạn có thể đổi tên hiển thị của nó).

   ![Chọn dự án](images/guide/b10-01-chon-du-an.png)

2. Chọn dự án khác trong ô đó, ví dụ **HRM**. Cả trang được dựng lại cho HRM: cây bộ test chỉ có "Chấm công" và danh sách chỉ có test case của HRM. Dự án vừa chọn được nhớ cho lần mở sau.

   ![Dự án HRM](images/guide/b10-02-du-an-hrm.png)

3. Người có quyền quản lý dự án thấy nút **Manage projects**. Trang **Projects** liệt kê các dự án cùng nội dung của từng dự án (số bộ test, test case, plan, yêu cầu, run).

   ![Trang quản lý dự án](images/guide/b10-03-trang-du-an.png)

4. Bấm **New project**. **Key** gồm 2 đến 10 chữ in hoa hoặc chữ số, bắt đầu bằng chữ cái (ví dụ `FIN`), **không đổi được sau này**; nên đặt trùng khóa dự án trong Jira nếu công ty dùng Jira. Bấm **Save**: dự án mới được chọn ngay.

   ![Tạo dự án](images/guide/b10-04-tao-du-an.png)

5. Dự án mới còn trống. Bộ test, test case, plan bạn tạo bây giờ đều thuộc dự án này, không ảnh hưởng dự án khác.

   ![Dự án mới có một bộ test](images/guide/b10-05-du-an-moi-co-suite.png)

6. Các dự án **không lẫn nhau**. Khi tạo run trong HRM, danh sách test case để chọn chỉ có test case đã duyệt của HRM; plan để chọn chỉ có plan của HRM. Hệ thống cũng từ chối các thao tác chéo dự án (chuyển bộ test sang dự án khác, đưa test case vào run của dự án khác, gắn test case vào yêu cầu của dự án khác).

   ![Run chỉ thấy test case của HRM](images/guide/b10-06-run-chi-thay-test-hrm.png)

7. Dashboard cũng theo dự án: số liệu của EasyInvoice không trộn với HRM.

   ![Dashboard theo dự án](images/guide/b10-07-dashboard-theo-du-an.png)

8. Dự án đã xong việc thì **Archive**. Dự án đã lưu trữ vẫn xem được nhưng không thêm được gì vào, và ẩn khỏi ô chọn (tích **Show archived projects** để thấy lại và **Restore**). Dự án còn trống mới xóa được; dự án đã có dữ liệu thì chỉ lưu trữ.

   ![Lưu trữ dự án](images/guide/b10-08-luu-tru-du-an.png)

> **Lưu ý:** mã test case và mã yêu cầu vẫn là duy nhất trong **cả hệ thống**, nên mỗi dự án dùng tiền tố riêng (`EINV-`, `HRM-`). Bước dùng chung và cổng chất lượng dùng chung cho mọi dự án. Hiện chưa phân quyền theo dự án: ai được xem test case thì xem được ở mọi dự án.

## 5. Quy ước của đội

> Đây là **đề xuất khởi đầu**. Cả đội thống nhất và chỉnh sửa trong buổi đầu, rồi cập nhật ngay vào mục này.

**Đặt mã test case:** `<DỰ ÁN>-<NHÓM>-<SỐ>`, ví dụ `EINV-INV-001`. Mã là duy nhất trong cả hệ thống, nên dự án nào cũng phải có tiền tố riêng.

**Tên test case:** viết như một câu nói được điều cần kiểm tra: *"Hủy hóa đơn đã phát hành và gửi thông báo sai sót"*, không viết *"Test hủy"*.

**Độ ưu tiên và mức nghiêm trọng:**

| Mức | Dùng khi |
|---|---|
| Urgent / Critical | Luồng chính, lỗi là không dùng được sản phẩm hoặc sai tiền thuế |
| High | Luồng quan trọng, có cách làm khác tạm thời |
| Medium | Chức năng phụ |
| Low | Chi tiết giao diện, trường hợp hiếm |

**Tag thường dùng:** `smoke` (bộ kiểm tra nhanh trước mỗi bản build), `hồi quy`, `api`, `bảo mật`. Thêm tag mới khi cần nhưng không tạo hai tag cùng nghĩa.

**Khi nào được duyệt (Approved):** có đủ bước rõ ràng, mỗi bước có kết quả mong đợi kiểm tra được, có dữ liệu thử cụ thể, người duyệt **khác** người viết.

**Ghi kết quả:**
- **Failed:** luôn viết kết quả thực tế cụ thể; gắn khóa lỗi Jira; đính kèm log hoặc ảnh khi có.
- **Blocked:** dùng khi **không chạy được** vì lý do bên ngoài (môi trường hỏng, chờ chức năng khác); ghi lý do.
- **Skipped:** cố ý không chạy trong đợt này; ghi lý do.

**Đặt tên plan và run:**
- Plan: `<Dự án> <Phiên bản> - <Ghi chú>`, ví dụ *"EasyInvoice 2.5 - Phát hành tháng 11"*.
- Run: `<Loại> build <số build>`, ví dụ *"Smoke build 2.5.0-rc1"*.

**Khi nào tạo run mới:** mỗi bản build đưa lên kiểm thử là một run. Chạy lại trong cùng một build thì dùng **Retest**, không tạo run mới.

**Nhiều dự án dùng chung hệ thống:** mỗi dự án một bộ test cấp cao nhất (ví dụ `[HRM] ...`), mã có tiền tố riêng, plan riêng cho từng đợt phát hành.

## 6. Hỏi đáp và xử lý sự cố

**Tôi không thấy mục Test Case Management ở thanh bên.**
Tài khoản chưa có quyền. Nhờ quản trị viên cấp quyền cho vai trò của bạn ở Administration → Roles.

**Tôi không thấy nút Approve (hoặc Create, Delete...).**
Quyền duyệt, tạo, xóa được cấp riêng. Xem [phụ lục](#phụ-lục-quyền-theo-vai-trò) và nhờ quản trị viên điều chỉnh.

**Tôi sửa test case đã duyệt, sao nó thành "Under review" và chưa thêm vào run được?**
Đúng thiết kế: sửa test case đã duyệt thì nó phải được duyệt lại. Trong lúc chờ, các run đang có vẫn dùng bản đã duyệt cũ. Khi người có quyền duyệt bấm **Approve**, test case có phiên bản mới (Version 2, 3...) và dùng được cho run mới.

**Tôi không thêm được test case vào run.**
Chỉ test case ở trạng thái **Approved** mới được thêm. Một test case cũng chỉ xuất hiện **một lần** trong một run.

**Cột Tester chỉ ghi "Assigned", không có tên người.**
Bình thường với người không có quyền xem danh sách người dùng (ví dụ Tester). Bộ lọc **My tests** vẫn dùng được.

**Tôi là QA Lead nhưng không có ô chọn người test ở cột Tester.**
Module cần đọc danh sách người dùng của ứng dụng. Trong ứng dụng ABP, vai trò của bạn phải có quyền xem danh sách người dùng (`AbpIdentity.Users`). Nhờ quản trị viên cấp quyền này ở Administration → Roles, hoặc cung cấp danh sách riêng nếu ứng dụng của bạn cấu hình khác.

**Tôi lỡ ghi nhầm kết quả.**
Không sửa được lần thử cũ (lịch sử chỉ thêm, không sửa). Hãy bấm **Retest** và ghi kết quả đúng; lần mới nhất là trạng thái hiện tại.

**Test "chập chờn" (flaky) là gì và làm gì với nó?**
Test lúc Pass lúc Fail dù code không đổi. Nó làm sai tỷ lệ đạt và làm mất niềm tin vào kết quả. Hãy báo cho đội để sửa test (thường do chờ phần tử chưa kịp hiện, phụ thuộc dịch vụ ngoài, dữ liệu dùng chung); đừng chỉ chạy lại cho qua.

**Cổng chất lượng báo không đạt nhưng tôi thấy test đã pass hết.**
Cổng còn xét **lỗi đang mở** (Critical/High) và việc **mọi test ưu tiên P1 đã chạy**, không chỉ tỷ lệ đạt. Xem từng dòng tiêu chí và danh sách lỗi mở ở dưới.

**Gợi ý bằng AI báo "could not be reached".**
Dịch vụ AI không phản hồi hoặc cấu hình sai. Chưa có thay đổi nào được lưu. Thử lại sau, và báo quản trị viên kiểm tra cấu hình AI nếu lỗi kéo dài.

**Nội dung tôi nhập cho AI đi đâu?**
Đoạn yêu cầu bạn nhập được gửi tới dịch vụ AI mà quản trị viên đã cấu hình. Đừng dán dữ liệu mật hoặc dữ liệu cá nhân của khách hàng vào.

---

## Phụ lục: quyền theo vai trò

Bảng dưới là bộ quyền của **ba vai trò dùng trong các bài tập** (cấu hình ở **Administration → Roles**).

| Việc | Tester | QA Lead | Product Owner |
|---|:---:|:---:|:---:|
| Xem test case, plan, run, dashboard, truy vết | ✔ | ✔ | ✔ |
| Viết và sửa test case | ✔ | ✔ | |
| Gợi ý bước bằng AI | ✔ | ✔ | |
| Duyệt, xóa test case | | ✔ | |
| Quản lý bộ test, bước dùng chung | | ✔ | |
| Tạo, đổi tên, lưu trữ dự án (`Projects.Manage`) | | ✔ | |
| Chạy test, ghi kết quả, gắn lỗi, đính kèm | ✔ | ✔ | |
| Lập plan và run, giao việc, hoàn tất run | | ✔ | |
| Quản lý yêu cầu, gắn test case vào yêu cầu | | ✔ | ✔ |
| Cấu hình cổng chất lượng | | ✔ | |
| Duyệt sign-off | | ✔ | ✔ |
| Xem danh sách người dùng để giao việc (`AbpIdentity.Users`) | | ✔ | |

