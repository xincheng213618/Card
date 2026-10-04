(function (doc, win) {
    var remChange = function () {
        var docEl = doc.documentElement,
            resizeEvt =
                "orientationchange" in window ? "orientationchange" : "resize",
            recalc = function () {
                var clientWidth = docEl.clientWidth;
                if (!clientWidth) return;
                if (clientWidth >= 580) {
                    docEl.style.fontSize = "75px";
                } else {
                    docEl.style.fontSize = 100 * (clientWidth / 750) + "px";
                }

            };
        if (!doc.addEventListener) return;
        win.addEventListener(resizeEvt, recalc, false);
        doc.addEventListener("DOMContentLoaded", recalc, false);
    }
    // 适配屏幕
    let timer = null;
    remChange()
    window.onresize = function () {
        if (timer) {
            if (window.innerWidth <= 750) {
                remChange()
            }
            clearTimeout(timer);
        }
        timer = setTimeout(remChange, 200)
    }




    // 左侧下载框
    var leftNewsSwiper = new Swiper(".left-news-swiper", {
        autoplay: {
            delay: 2000,//2秒切换一次
        },
    });

    $('.left-dl-show-btn').click(function () {
        if ($(this).hasClass('turn')) {
            $(this).removeClass('turn');
            $('.left-dl-box').removeClass('show');
        } else {
            $(this).addClass('turn');
            $('.left-dl-box').addClass('show');
        }
    })


})(document, window);

var baseUrl = ''
var public = {
    ajaxApi: function (type, url, data, callback) {
        $.ajax({
            url: baseUrl + url,
            type: type,
            // xhrFields: {
            //     'withCredentials': true, //必须开启这个参数才会传递cookie
            // },
            dataType: 'json',
            data: data,
            // headers: {
            //     'X-Requested-With': 'XMLHttpRequest',
            // 'Authorization': 'bearer ' + token
            // },
            success: function (data) {
                typeof callback == 'function' && callback(data);
            },
            error: function (xhr, type) {
                // if (xhr.status == 302 || xhr.status == 304) {
                //     window.location.href = xhr.responseText;
                // } else if (xhr.status == 429) {
                //     alert("请求频繁，请稍后再试！")
                // } else if (xhr.status == 419) {
                //     alert("请求参数错误！")
                // } else if (xhr.status == 500) {
                //     alert("系统繁忙，请稍后再试！")
                // }
                // alert(xhr.msg)
                console.log(xhr.msg);
            }
        });
    },
    getUrlParameter(sParam) {
        var sPageURL = decodeURIComponent(window.location.search.substring(1)),
            sURLVariables = sPageURL.split('&'),
            sParameterName,
            i;
        for (i = 0; i < sURLVariables.length; i++) {
            sParameterName = sURLVariables[i].split('=');
            if (sParameterName[0] === sParam) {
                return sParameterName[1] === undefined ? true : sParameterName[1];
            }
        }
    },
    replaceParamVal(paramName, replaceWith) {
        var oUrl = window.location.href.toString();
        var re = eval('/(' + paramName + '=)([^&]*)/gi');
        var nUrl = oUrl.replace(re, paramName + '=' + replaceWith);
        this.location = nUrl;
        window.location.href = nUrl
    },
    getSystem() {
        const userAgent = window.navigator.userAgent;
        if (userAgent.indexOf('Mac OS X') !== -1) {
            return 'macOS';
        } else if (userAgent.indexOf('Windows') !== -1) {
            return 'Windows';
        } else {
            return 'Other';
        }
    },
}

var getUrlParameter = function (sParam) {
    var sPageURL = decodeURIComponent(window.location.search.substring(1)),
        sURLVariables = sPageURL.split('&'),
        sParameterName,
        i;
    for (i = 0; i < sURLVariables.length; i++) {
        sParameterName = sURLVariables[i].split('=');
        if (sParameterName[0] === sParam) {
            return sParameterName[1] === undefined ? true : sParameterName[1];
        }
    }
}

var reportVideo = function(data){
    public.ajaxApi("post", `${window.location.origin}/api/v1/report.json`, data, function (res) {})
}